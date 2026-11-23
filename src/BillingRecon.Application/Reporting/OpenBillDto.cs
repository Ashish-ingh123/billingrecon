namespace BillingRecon.Application.Reporting;

/// <summary>A posted bill that still owes something: when it was due, and what it owes.</summary>
public sealed record OpenBillDto(DateOnly DueDate, decimal BalanceDue);
