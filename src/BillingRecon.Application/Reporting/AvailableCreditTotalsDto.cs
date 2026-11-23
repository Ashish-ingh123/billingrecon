namespace BillingRecon.Application.Reporting;

/// <summary>Every credit note of the report together; <see cref="AvailableAmount"/> equals the balance of the CreditAvailable account (BR-901).</summary>
public sealed record AvailableCreditTotalsDto(int CreditNotes, decimal AvailableAmount);
