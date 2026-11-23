namespace BillingRecon.Application.Reporting;

/// <summary>The business dates a report covers, both included: calendar dates in India (BR-003).</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
public sealed record ReportPeriodDto(DateOnly From, DateOnly To);
