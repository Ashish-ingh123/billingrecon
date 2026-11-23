namespace BillingRecon.Application.Reporting;

/// <summary>Every waiting request of the report together: how many, and since when the oldest has waited.</summary>
public sealed record PendingApprovalsTotalsDto(int Requests, DateTimeOffset? OldestRequestedAtUtc);
