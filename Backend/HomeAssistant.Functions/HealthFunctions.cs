using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HomeAssistant.Functions;

public class HealthFunctions
{
    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "head", Route = "health")] HttpRequest req)
    {
        return new OkObjectResult(new { status = "healthy" });
    }

    [Function("Version")]
    public IActionResult Version(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "version")] HttpRequest req)
    {
        string version = Environment.GetEnvironmentVariable("DEPLOYMENT_VERSION") ?? "local";
        return new OkObjectResult(new { version });
    }
}
