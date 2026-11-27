using System.Reflection;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Common;

/// <summary>Non-business endpoints that describe the running service.</summary>
[Route(ApiRoutes.V1 + "/system")]
public sealed class SystemController(IHostEnvironment environment, IClock clock) : ApiControllerBase
{
    private static readonly string Version =
        typeof(SystemController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Returns the product name, the API version and the hosting environment.</summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo()
        => Ok(new SystemInfoResponse("BillingRecon", Version, environment.EnvironmentName));

    /// <summary>
    /// Today in India by the server's clock (BR-003): the day a list's date shortcuts count from (Prompt 12.2,
    /// ADR 0063). Anyone signed in; never cached.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("business-date")]
    [ProducesResponseType<BusinessDateResponse>(StatusCodes.Status200OK)]
    public ActionResult<BusinessDateResponse> GetBusinessDate()
    {
        Response.Headers.CacheControl = "no-store";

        return Ok(new BusinessDateResponse(clock.TodayIst));
    }
}
