using HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using NetDaemon.Client;
using NetDaemon.Client.Extensions;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// Sends commands through NetDaemon's real websocket client to a stand-in Home Assistant, so that what NetDaemon does
/// with HA's answer is what is tested. On 2026-10-21 the retry layer was tested only against a fake that threw, while
/// the NetDaemon call it used never waited for the answer, so no failure was ever retried (HOME-ASSISTANT.md gotcha 24).
/// </summary>
[TestClass]
public sealed class ServiceCallAgainstRealClientTests
{
    private static readonly ServiceCall FingerbotOn = new("switch", "turn_on", ServiceTarget.FromEntity("switch.tz3210_j4pdtz9v_ts0001"), null);

    [TestMethod]
    public async Task AnErrorAnswer_IsAFailure()
    {
        // The answer HA gave at 21:01:33 on 2026-10-21.
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync(_ => """{"code":"home_assistant_error","message":"Failed to send request: device did not respond"}""");
        ServiceCallRetrier retrier = await ha.CreateRetrierAsync();

        (await retrier.SendOnceAsync(FingerbotOn)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task ASuccessAnswer_IsASuccess_AndTheCommandIsWhatHomeAssistantExpects()
    {
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync(_ => null);
        ServiceCallRetrier retrier = await ha.CreateRetrierAsync();

        (await retrier.SendOnceAsync(new ServiceCall("climate", "set_temperature", ServiceTarget.FromEntity("climate.den_radiator_1_thermostat"), new { temperature = 25.4 }))).ShouldBeTrue();

        JsonElement sent = ha.ServiceCalls.Single();
        sent.GetProperty("domain").GetString().ShouldBe("climate");
        sent.GetProperty("service").GetString().ShouldBe("set_temperature");
        sent.GetProperty("service_data").GetProperty("temperature").GetDouble().ShouldBe(25.4);
        sent.GetProperty("target").GetProperty("entity_id")[0].GetString().ShouldBe("climate.den_radiator_1_thermostat");
    }

    [TestMethod]
    [Timeout(60000)]
    public async Task NoAnswerAtAll_IsAFailure()
    {
        // 02:42 on 2026-10-22: HA did not answer the fingerbot's turn_on within NetDaemon's 20 seconds, and the
        // switch then reported "on" with the boiler never pressed.
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync(_ => FakeHomeAssistant.NoAnswer);
        ServiceCallRetrier retrier = await ha.CreateRetrierAsync();

        (await retrier.SendOnceAsync(FingerbotOn)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task AFailedCall_IsRetried_UntilHomeAssistantAccepts()
    {
        int answered = 0;
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync(_ =>
            Interlocked.Increment(ref answered) == 1 ? """{"code":"home_assistant_error","message":"Failed to send request: device did not respond"}""" : null);
        ServiceCallRetrier retrier = await ha.CreateRetrierAsync();

        await retrier.Send(FingerbotOn);

        ha.ServiceCalls.Count.ShouldBe(2, "sent, failed, sent again");
    }

    /// <summary>
    /// Just enough of Home Assistant's websocket API for NetDaemon's client: authentication, then an answer to every
    /// command. <c>call_service</c> commands get whatever the test's function says: null for success, an error object
    /// as JSON, or <see cref="NoAnswer"/>.
    /// </summary>
    private sealed class FakeHomeAssistant : IAsyncDisposable
    {
        public const string NoAnswer = "no answer";

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Func<JsonElement, string?> _answerServiceCall;
        private readonly ConcurrentQueue<JsonElement> _serviceCalls = new();
        private ServiceProvider? _clientServices;
        private IHomeAssistantConnection? _connection;
        private int _port;

        private FakeHomeAssistant(Func<JsonElement, string?> answerServiceCall)
        {
            _answerServiceCall = answerServiceCall;
        }

        public IReadOnlyList<JsonElement> ServiceCalls => [.. _serviceCalls];

        public static Task<FakeHomeAssistant> StartAsync(Func<JsonElement, string?> answerServiceCall)
        {
            FakeHomeAssistant ha = new(answerServiceCall);
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            ha._port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            ha._listener.Prefixes.Add($"http://localhost:{ha._port}/");
            ha._listener.Start();
            _ = ha.AcceptAsync();
            return Task.FromResult(ha);
        }

        /// <summary>A retrier sending through NetDaemon's real client, connected to this stand-in.</summary>
        public async Task<ServiceCallRetrier> CreateRetrierAsync()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddHomeAssistantClient();
            _clientServices = services.BuildServiceProvider();
            IHomeAssistantClient client = _clientServices.GetRequiredService<IHomeAssistantClient>();
            _connection = await client.ConnectAsync("localhost", _port, false, "test-token", "api/websocket", _stop.Token);

            IHomeAssistantRunner runner = Substitute.For<IHomeAssistantRunner>();
            runner.CurrentConnection.Returns(_connection);
            // Retries in these tests wait no time at all.
            return new ServiceCallRetrier(
                call => ServiceCallRetrier.SendThroughRunnerAsync(runner, call),
                _ => Task.CompletedTask,
                new RecordingLogger<ServiceCallRetrier>());
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    HttpListenerWebSocketContext socketContext = await context.AcceptWebSocketAsync(null);
                    _ = ServeAsync(socketContext.WebSocket);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ServeAsync(WebSocket socket)
        {
            try
            {
                await SendAsync(socket, """{"type":"auth_required","ha_version":"2026.10.0"}""");
                await ReceiveAsync(socket);
                await SendAsync(socket, """{"type":"auth_ok","ha_version":"2026.10.0"}""");

                while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    string? text = await ReceiveAsync(socket);
                    if (text == null)
                        return;

                    using JsonDocument document = JsonDocument.Parse(text);
                    JsonElement message = document.RootElement.Clone();
                    if (!message.TryGetProperty("id", out JsonElement idElement))
                        continue;

                    int id = idElement.GetInt32();
                    string type = message.GetProperty("type").GetString()!;
                    string? error = null;
                    if (type == "call_service")
                    {
                        _serviceCalls.Enqueue(message);
                        error = _answerServiceCall(message);
                        if (error == NoAnswer)
                            continue;
                    }

                    // NetDaemon's connect checks that HA reports itself running.
                    string result = type == "get_config" ? """{"state":"RUNNING","version":"2026.10.0","components":[]}""" : "null";
                    await SendAsync(socket, error == null
                        ? $$"""{"id":{{id}},"type":"result","success":true,"result":{{result}}}"""
                        : $$"""{"id":{{id}},"type":"result","success":false,"error":{{error}}}""");
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested || socket.State != WebSocketState.Open)
            {
            }
        }

        private async Task SendAsync(WebSocket socket, string text) =>
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, _stop.Token);

        private async Task<string?> ReceiveAsync(WebSocket socket)
        {
            byte[] buffer = new byte[64 * 1024];
            using MemoryStream received = new();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                received.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(received.ToArray());
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_connection != null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch (Exception)
                {
                }
            }

            if (_clientServices != null)
            {
                await _clientServices.DisposeAsync();
            }

            _listener.Close();
            _stop.Dispose();
        }
    }
}
