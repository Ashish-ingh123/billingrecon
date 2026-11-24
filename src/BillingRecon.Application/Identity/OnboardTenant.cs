using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A hospital as the operator onboards it (ADR 0052): its code and name, its first administrator, and its settings
/// (BR-607) and default prefixes (BR-802). A prefix left out keeps the standard one.
/// </summary>
public sealed record OnboardTenant
{
    /// <summary>The tenant's code: lower-case letters and digits with single hyphens; unique.</summary>
    public required string Code { get; init; }

    /// <summary>The hospital's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The first administrator's e-mail address, with which they sign in.</summary>
    public required string AdminEmail { get; init; }

    /// <summary>The first administrator's name.</summary>
    public required string AdminDisplayName { get; init; }

    /// <summary>A bill whose total reaches this amount needs approval; null: none does (BR-207).</summary>
    public decimal? PostingApprovalThreshold { get; init; }

    /// <summary>How many days before today a business date may lie (BR-205).</summary>
    public required int MaxBackdateDays { get; init; }

    /// <summary>Days between the bill date and the default due date (BR-205).</summary>
    public required int DefaultDueDays { get; init; }

    /// <summary>The default prefix of each document type that is not the standard one.</summary>
    public IReadOnlyDictionary<DocumentType, string> Prefixes { get; init; } = new Dictionary<DocumentType, string>();
}
