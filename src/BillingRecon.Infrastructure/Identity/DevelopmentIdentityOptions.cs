namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The fixed tenant and user that every request acts for in the Development environment, until authentication
/// arrives (Phase 5). Bound from the <c>DevelopmentIdentity</c> configuration section, which exists in
/// <c>appsettings.Development.json</c> only. The values are not secrets.
/// </summary>
public sealed class DevelopmentIdentityOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "DevelopmentIdentity";

    /// <summary>Message used when either id is missing.</summary>
    public const string IncompleteMessage =
        "DevelopmentIdentity:TenantId and DevelopmentIdentity:UserId are both required in the Development environment (two GUIDs, see appsettings.Development.json).";

    /// <summary>The tenant every request acts for.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The user every request acts as.</summary>
    public Guid UserId { get; init; }

    /// <summary>True when both ids are set.</summary>
    public static bool IsComplete(DevelopmentIdentityOptions options) => options.TenantId != Guid.Empty && options.UserId != Guid.Empty;
}
