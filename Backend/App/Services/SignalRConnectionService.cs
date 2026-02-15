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
    /// Fired when the connection is restored after a disconnection.
    /// Consumers should refresh any data they may have missed.
    /// </summary>
    event Func<Task>? ConnectionRestored;

    /// <summary>
    /// Starts the SignalR connection (negotiate, connect, add to group).
    /// Safe to call multiple times — only the first call connects.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Registers a handler for a SignalR message. Can be called before or after StartAsync.
    /// Handlers are permanently registered and replayed onto any new connection.
    /// </summary>
    void On<T>(string methodName, Func<T, Task> handler);
}

internal class SignalRConnectionService : ISignalRConnectionService
{
    private readonly ILogger<SignalRConnectionService> _logger;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly List<Action<HubConnection>> _handlerRegistry = new();
    private readonly object _handlerLock = new();

    private static readonly int[] ReconnectBackoffSeconds = [5, 10, 30, 60];

    private HubConnection? _hubConnection;
    private bool _started;
    private bool _isReconnecting;

    public event Func<Task>? ConnectionRestored;

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
        Action<HubConnection> registrationAction = hub => hub.On(methodName, handler);

        lock (_handlerLock)
        {
            // Always store in the permanent registry
            _handlerRegistry.Add(registrationAction);

            if (_hubConnection != null)
            {
                // Connection already exists — also register directly
                registrationAction(_hubConnection);
                _logger.LogDebug("Registered handler for '{MethodName}' on live connection", methodName);
            }
            else
            {
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

            HubConnection hub = BuildHubConnection(hubUrl, accessToken);

            // Register all handlers from the permanent registry
            lock (_handlerLock)
            {
                _hubConnection = hub;
                foreach (Action<HubConnection> register in _handlerRegistry)
                {
                    register(hub);
                }
            }

            WireHubEvents(hub);

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

    private HubConnection BuildHubConnection(string hubUrl, string accessToken)
    {
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .WithAutomaticReconnect()
            .Build();
    }

    private void WireHubEvents(HubConnection hub)
    {
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

            // Auto-reconnect has given up — start the resilient reconnect loop
            _ = Task.Run(() => ReconnectLoopAsync());
        };

        hub.Reconnecting += (Exception? error) =>
        {
            _logger.LogWarning(error, "SignalR connection lost, attempting automatic reconnect");
            return Task.CompletedTask;
        };

        hub.Reconnected += async (string? connectionId) =>
        {
            _logger.LogInformation("SignalR auto-reconnected with connection ID: {ConnectionId}", connectionId);

            if (!string.IsNullOrEmpty(connectionId))
            {
                try
                {
                    await AddToGroupAsync(connectionId);
                    _logger.LogInformation("Successfully re-added to SignalR group after auto-reconnection");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to re-add to SignalR group after auto-reconnection");
                }
            }

            // Even brief disconnections can miss messages — notify consumers
            await FireConnectionRestoredAsync();
        };
    }

    private async Task ReconnectLoopAsync()
    {
        if (_isReconnecting)
        {
            _logger.LogDebug("Reconnect loop already running, skipping");
            return;
        }

        _isReconnecting = true;
        int attempt = 0;

        try
        {
            _logger.LogWarning("Starting resilient reconnect loop for house {HouseId}", _configuration.HouseId);

            while (true)
            {
                int backoffIndex = Math.Min(attempt, ReconnectBackoffSeconds.Length - 1);
                int delaySeconds = ReconnectBackoffSeconds[backoffIndex];
                attempt++;

                _logger.LogInformation("Reconnect attempt {Attempt} in {Delay}s", attempt, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                try
                {
                    // Dispose old connection
                    HubConnection? oldHub;
                    lock (_handlerLock)
                    {
                        oldHub = _hubConnection;
                        _hubConnection = null;
                    }

                    if (oldHub != null)
                    {
                        try
                        {
                            await oldHub.DisposeAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error disposing old hub connection (non-critical)");
                        }
                    }

                    // Re-negotiate for a fresh token
                    _logger.LogDebug("Re-negotiating SignalR connection for attempt {Attempt}", attempt);
                    string connectionInfoJson = await NegotiateAsync();

                    using JsonDocument doc = JsonDocument.Parse(connectionInfoJson);
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("url", out JsonElement urlElement) ||
                        !root.TryGetProperty("accessToken", out JsonElement tokenElement))
                    {
                        _logger.LogWarning("Reconnect attempt {Attempt}: negotiate response missing url or accessToken", attempt);
                        continue;
                    }

                    string hubUrl = urlElement.GetString() ?? string.Empty;
                    string accessToken = tokenElement.GetString() ?? string.Empty;

                    if (string.IsNullOrEmpty(hubUrl) || string.IsNullOrEmpty(accessToken))
                    {
                        _logger.LogWarning("Reconnect attempt {Attempt}: negotiate response has empty url or accessToken", attempt);
                        continue;
                    }

                    // Build new connection and replay all handlers
                    HubConnection newHub = BuildHubConnection(hubUrl, accessToken);

                    lock (_handlerLock)
                    {
                        _hubConnection = newHub;
                        foreach (Action<HubConnection> register in _handlerRegistry)
                        {
                            register(newHub);
                        }
                    }

                    WireHubEvents(newHub);

                    await newHub.StartAsync();
                    _logger.LogInformation("Reconnect attempt {Attempt} succeeded (ConnectionId: {ConnectionId})",
                        attempt, newHub.ConnectionId);

                    // Re-add to group
                    if (!string.IsNullOrEmpty(newHub.ConnectionId))
                    {
                        try
                        {
                            await AddToGroupAsync(newHub.ConnectionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to add reconnected connection to group");
                        }
                    }

                    // Notify consumers to refresh their data
                    await FireConnectionRestoredAsync();

                    _logger.LogInformation("SignalR resilient reconnection complete for house {HouseId}", _configuration.HouseId);
                    return; // Success — exit the loop
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reconnect attempt {Attempt} failed, will retry", attempt);
                }
            }
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    private async Task FireConnectionRestoredAsync()
    {
        if (ConnectionRestored != null)
        {
            try
            {
                await ConnectionRestored.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ConnectionRestored handler");
            }
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
