namespace BillingRecon.Api.Common;

/// <summary>Describes the running service.</summary>
/// <param name="Application">Product name.</param>
/// <param name="Version">Informational version of the API assembly.</param>
/// <param name="Environment">ASP.NET Core environment name.</param>
public sealed record SystemInfoResponse(string Application, string Version, string Environment);
