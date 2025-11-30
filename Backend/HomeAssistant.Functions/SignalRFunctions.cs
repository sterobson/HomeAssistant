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
        [SignalRConnectionInfoInput(HubName = "homeassistant", UserId = "{query.houseId}")] string connectionInfo)
    {
        _logger.LogInformation("SignalR negotiation request received");

        HttpResponseData response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(connectionInfo);

        return response;
    }
}
