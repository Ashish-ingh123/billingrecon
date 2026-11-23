namespace BillingRecon.Application.Reporting;

/// <summary>When a maker-checker request was asked for, and when it was decided (null while it waits).</summary>
public sealed record ApprovalSpanDto(DateTimeOffset RequestedAtUtc, DateTimeOffset? DecidedAtUtc);
