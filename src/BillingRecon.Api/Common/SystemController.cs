using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Common;

/// <summary>Non-business endpoints that describe the running service.</summary>
[Route(ApiRoutes.V1 + "/system")]
public sealed class SystemController(IHostEnvironment environment) : ApiControllerBase
{
    private static readonly string Version =
        typeof(SystemController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Returns the product name, the API version and the hosting environment.</summary>
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo()
        => Ok(new SystemInfoResponse("BillingRecon", Version, environment.EnvironmentName));
}
