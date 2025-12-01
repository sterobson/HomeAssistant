using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

public class SignalRService
{
    private readonly ILogger<SignalRService> _logger;
    private readonly Lazy<Task<ServiceManager?>> _serviceManagerTask;
    private readonly bool _isEnabled;

    public SignalRService(ILogger<SignalRService> logger)
    {
        _logger = logger;

        // Check if SignalR is configured
        string? connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
        _isEnabled = !string.IsNullOrEmpty(connectionString);

        if (!_isEnabled)
        {
            _logger.LogWarning("AzureSignalRConnectionString not configured - SignalR messaging disabled");
        }

        _serviceManagerTask = new Lazy<Task<ServiceManager?>>(InitializeServiceManagerAsync);
    }

    private async Task<ServiceManager?> InitializeServiceManagerAsync()
    {
        if (!_isEnabled)
        {
            return null;
        }

        string? connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        var serviceManager = new ServiceManagerBuilder()
            .WithOptions(option => option.ConnectionString = connectionString)
            .BuildServiceManager();

        return await Task.FromResult(serviceManager);
    }

    private async Task<ServiceManager?> GetServiceManagerAsync()
    {
        return await _serviceManagerTask.Value;
    }

    public async Task SendMessageToUserAsync(string userId, string message, object data)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("SignalR disabled - skipping message {Message} to user {UserId}", message, userId);
            return;
        }

        try
        {
            _logger.LogInformation("Sending message {Message} to user {UserId}", message, userId);
            var serviceManager = await GetServiceManagerAsync();
            if (serviceManager == null)
            {
                _logger.LogWarning("SignalR service manager not available");
                return;
            }

            var hubContext = await serviceManager.CreateHubContextAsync("homeassistant", default);
            await hubContext.Clients.User(userId).SendCoreAsync(message, new object[] { data });
            _logger.LogInformation("Successfully sent message {Message} to user {UserId}", message, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {Message} to user {UserId}", message, userId);
            // Don't throw - just log the error
        }
    }

    public async Task SendMessageToGroupAsync(string groupName, string message, object data)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("SignalR disabled - skipping message {Message} to group {GroupName}", message, groupName);
            return;
        }

        try
        {
            _logger.LogInformation("Attempting to send SignalR message '{Message}' to group '{GroupName}' with data: {Data}",
                message, groupName, System.Text.Json.JsonSerializer.Serialize(data));

            Microsoft.Azure.SignalR.Management.ServiceManager? serviceManager = await GetServiceManagerAsync();
            if (serviceManager == null)
            {
                _logger.LogWarning("SignalR service manager not available - cannot send message");
                return;
            }

            Microsoft.Azure.SignalR.Management.IServiceHubContext hubContext = await serviceManager.CreateHubContextAsync("homeassistant", default);
            await hubContext.Clients.Group(groupName).SendCoreAsync(message, new object[] { data });
            _logger.LogInformation("Successfully sent SignalR message '{Message}' to group '{GroupName}'", message, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR message '{Message}' to group '{GroupName}'", message, groupName);
            // Don't throw - just log the error
        }
    }
}
