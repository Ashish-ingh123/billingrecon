namespace BillingRecon.Api.Common;

/// <summary>Route prefixes. Every controller route starts with a version prefix (docs/adr/0013-api-versioning-by-route-prefix.md).</summary>
public static class ApiRoutes
{
    /// <summary>Version 1 of the API. A breaking change gets a new prefix; routes under this one never break.</summary>
    public const string V1 = "api/v1";
}
