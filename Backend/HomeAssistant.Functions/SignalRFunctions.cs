using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions;

public class SignalRFunctions
{
    private readonly ILogger<SignalRFunctions> _logger;

    public SignalRFunctions(ILogger<SignalRFunctions> logger)
    {
        _logger = logger;
    }

    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "signalr/negotiate")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "homeassistant")] string connectionInfo)
    {
        string? houseId = req.Query["houseId"];
        _logger.LogInformation("SignalR negotiation request received for houseId: {HouseId}", houseId ?? "MISSING");

        // Parse and add group information to the connection
        System.Text.Json.JsonDocument connDoc = System.Text.Json.JsonDocument.Parse(connectionInfo);
        string url = connDoc.RootElement.GetProperty("url").GetString() ?? "";
        string accessToken = connDoc.RootElement.GetProperty("accessToken").GetString() ?? "";

        // Return connection info
        HttpResponseData response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(connectionInfo);

        return response;
    }

    [Function("add-to-group")]
    public async Task<HttpResponseData> AddToGroup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signalr/add-to-group")] HttpRequestData req)
    {
        string? houseId = req.Query["houseId"];
        string? connectionId = req.Query["connectionId"];

        if (string.IsNullOrWhiteSpace(houseId) || string.IsNullOrWhiteSpace(connectionId))
        {
            _logger.LogWarning("add-to-group missing houseId or connectionId");
            HttpResponseData badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("{\"error\": \"houseId and connectionId are required\"}");
            return badResponse;
        }

        try
        {
            string? connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureSignalRConnectionString not configured");
                HttpResponseData errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("{\"error\": \"SignalR not configured\"}");
                return errorResponse;
            }

            Microsoft.Azure.SignalR.Management.ServiceManager serviceManager = new Microsoft.Azure.SignalR.Management.ServiceManagerBuilder()
                .WithOptions(option => option.ConnectionString = connectionString)
                .BuildServiceManager();

            Microsoft.Azure.SignalR.Management.IServiceHubContext hubContext = await serviceManager.CreateHubContextAsync("homeassistant", default);

            // Try adding to group
            await hubContext.Groups.AddToGroupAsync(connectionId, $"house-{houseId}");
            _logger.LogInformation("Called Groups.AddToGroupAsync for connection {ConnectionId} to group house-{HouseId}", connectionId, houseId);

            // Send test messages to verify what works
            // 1. Direct to specific connection
            await hubContext.Clients.Client(connectionId).SendCoreAsync("test-direct-connection", new object[] { new { message = "Direct to connectionId", houseId, connectionId, timestamp = DateTimeOffset.UtcNow } });
            _logger.LogInformation("Sent test-direct-connection to connectionId {ConnectionId}", connectionId);

            // 2. To group (which we just added the connection to)
            await hubContext.Clients.Group($"house-{houseId}").SendCoreAsync("test-message", new object[] { new { message = "To group after adding", houseId, connectionId, timestamp = DateTimeOffset.UtcNow } });
            _logger.LogInformation("Sent test-message to group house-{HouseId} after adding connection {ConnectionId}", houseId, connectionId);

            HttpResponseData response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteStringAsync("{\"success\": true}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding connection to group");
            HttpResponseData errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("{\"error\": \"Failed to add to group\"}");
            return errorResponse;
        }
    }

    [Function("send-test-message")]
    public async Task<HttpResponseData> SendTestMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signalr/send-test-message")] HttpRequestData req)
    {
        string? houseId = req.Query["houseId"];

        if (string.IsNullOrWhiteSpace(houseId))
        {
            _logger.LogWarning("send-test-message missing houseId");
            HttpResponseData badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("{\"error\": \"houseId is required\"}");
            return badResponse;
        }

        try
        {
            string? connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureSignalRConnectionString not configured");
                HttpResponseData errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("{\"error\": \"SignalR not configured\"}");
                return errorResponse;
            }

            Microsoft.Azure.SignalR.Management.ServiceManager serviceManager = new Microsoft.Azure.SignalR.Management.ServiceManagerBuilder()
                .WithOptions(option => option.ConnectionString = connectionString)
                .BuildServiceManager();

            Microsoft.Azure.SignalR.Management.IServiceHubContext hubContext = await serviceManager.CreateHubContextAsync("homeassistant", default);

            // Send test messages to all possible targets
            object testData = new { message = "Manual test message", houseId, timestamp = DateTimeOffset.UtcNow };

            // Try sending to group
            await hubContext.Clients.Group($"house-{houseId}").SendCoreAsync("test-message", new object[] { testData });
            _logger.LogInformation("Sent test-message to GROUP house-{HouseId}", houseId);

            // Also try sending to all clients (for comparison)
            await hubContext.Clients.All.SendCoreAsync("test-message-all", new object[] { testData });
            _logger.LogInformation("Sent test-message-all to ALL clients");

            HttpResponseData response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteStringAsync($"{{\"success\": true, \"sentToGroup\": \"house-{houseId}\"}}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test message");
            HttpResponseData errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("{\"error\": \"Failed to send test message\"}");
            return errorResponse;
        }
    }
}
