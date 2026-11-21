using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The body of <c>PATCH /api/v1/settings</c>: every setting it changes, as the new state (docs/api.md section 5). A
/// threshold left out or null means none. The prefixes are not here: they change through a prefix change. Unknown
/// members are refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSettingsRequest
{
    /// <summary>A bill whose total reaches this needs a second person (BR-207); none when left out or null.</summary>
    public decimal? PostingApprovalThreshold { get; init; }

    /// <summary>How many days before today a business date may lie, 0 to 365 (BR-205).</summary>
    public int? MaxBackdateDays { get; init; }

    /// <summary>Days from the bill date to its default due date, 0 to 365 (BR-205).</summary>
    public int? DefaultDueDays { get; init; }

    /// <summary>The command, at the version the client read.</summary>
    public UpdateTenantSettingsCommand ToCommand(string expectedVersion) => new(PostingApprovalThreshold, MaxBackdateDays, DefaultDueDays, expectedVersion);
}
