using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HomeAssistant.Functions;

public class HealthFunctions
{
    // Read once at process start. The value is baked into the assembly at
    // build time via /p:DeploymentVersion=... (see csproj). Defaults to
    // "local" for unflagged dev builds.
    private static readonly string DeploymentVersion = Assembly
        .GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "DeploymentVersion")?.Value ?? "unknown";

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
        return new OkObjectResult(new { version = DeploymentVersion });
    }
}
