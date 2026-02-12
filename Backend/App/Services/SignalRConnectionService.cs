using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>
/// Manages a single shared SignalR connection for the application
/// </summary>
internal interface ISignalRConnectionService
{
    /// <summary>
    /// Starts the SignalR connection (negotiate, connect, add to group).
    /// Safe to call multiple times — only the first call connects.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Registers a handler for a SignalR message. Can be called before or after StartAsync.
    /// </summary>
    void On<T>(string methodName, Func<T, Task> handler);
}

internal class SignalRConnectionService : ISignalRConnectionService
{
    private readonly ILogger<SignalRConnectionService> _logger;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly List<Action<HubConnection>> _pendingHandlers = new();
    private readonly object _handlerLock = new();

    private HubConnection? _hubConnection;
    private bool _started;

    public SignalRConnectionService(
        ILogger<SignalRConnectionService> logger,
        WebSynchronisationConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public void On<T>(string methodName, Func<T, Task> handler)
    {
        lock (_handlerLock)
        {
            if (_hubConnection != null)
            {
                // Connection already exists — register directly
                _hubConnection.On(methodName, handler);
                _logger.LogDebug("Registered handler for '{MethodName}' on live connection", methodName);
            }
            else
            {
                // Queue for later registration
                _pendingHandlers.Add(hub => hub.On(methodName, handler));
                _logger.LogDebug("Queued handler for '{MethodName}' (connection not yet established)", methodName);
            }
        }
    }

    public async Task StartAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_started)
            {
                return;
            }

            await ConnectAsync();
            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(_configuration.HouseId) || string.IsNullOrEmpty(_configuration.ScheduleApiUrl))
        {
            _logger.LogWarning("SignalR not configured — HouseId or ScheduleApiUrl missing");
            return;
        }

        try
        {
            _logger.LogDebug("Negotiating SignalR connection for house {HouseId}", _configuration.HouseId);

            string connectionInfoJson = await NegotiateAsync();

            using JsonDocument doc = JsonDocument.Parse(connectionInfoJson);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("url", out JsonElement urlElement) ||
                !root.TryGetProperty("accessToken", out JsonElement tokenElement))
            {
                _logger.LogError("SignalR connection info missing required properties (url or accessToken)");
                return;
            }

            string hubUrl = urlElement.GetString() ?? string.Empty;
            string accessToken = tokenElement.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(hubUrl) || string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("SignalR connection info contains empty url or accessToken");
                return;
            }

            HubConnection hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            // Register all queued handlers
            lock (_handlerLock)
            {
                _hubConnection = hub;
                foreach (Action<HubConnection> register in _pendingHandlers)
                {
                    register(hub);
                }
                _pendingHandlers.Clear();
            }

            hub.Closed += async (Exception? error) =>
            {
                if (error != null)
                {
                    _logger.LogError(error, "SignalR connection closed with error");
                }
                else
                {
                    _logger.LogWarning("SignalR connection closed");
                }
            };

            hub.Reconnecting += (Exception? error) =>
            {
                _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect");
                return Task.CompletedTask;
            };

            hub.Reconnected += async (string? connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected with connection ID: {ConnectionId}", connectionId);

                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await AddToGroupAsync(connectionId);
                        _logger.LogInformation("Successfully re-added to SignalR group after reconnection");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-add to SignalR group after reconnection");
                    }
                }
            };

            await hub.StartAsync();
            _logger.LogInformation("Connected to SignalR (house {HouseId}, ConnectionId: {ConnectionId})",
                _configuration.HouseId, hub.ConnectionId);

            if (!string.IsNullOrEmpty(hub.ConnectionId))
            {
                try
                {
                    await AddToGroupAsync(hub.ConnectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add connection to group — will rely on fallback");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to SignalR for house {HouseId}", _configuration.HouseId);
        }
    }

    private async Task<string> NegotiateAsync()
    {
        HttpResponseMessage response = await _httpClient.PostAsync(
            $"/api/signalr/negotiate?houseId={_configuration.HouseId}", null);
        response.EnsureSuccessStatusCode();

        string connectionInfo = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Successfully negotiated SignalR connection for house {HouseId}", _configuration.HouseId);
        return connectionInfo;
    }

    private async Task AddToGroupAsync(string connectionId)
    {
        _logger.LogInformation("Adding connection {ConnectionId} to group for house {HouseId}",
            connectionId, _configuration.HouseId);

        HttpResponseMessage response = await _httpClient.PostAsync(
            $"/api/signalr/add-to-group?houseId={_configuration.HouseId}&connectionId={connectionId}", null);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Successfully added connection to SignalR group for house {HouseId}", _configuration.HouseId);
    }
}
