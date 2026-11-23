namespace BillingRecon.Application.Reporting;

/// <summary>
/// What a payer owes and what is held for them (US-40, BR-404): the balances due of their posted bills, the money they paid
/// that is not applied to a bill, the credit of their credit notes not yet used, and what they owe net of the two.
/// Each equals the sum of the payer's entries in its ledger account (docs/database.md section 5.5, identities 6 to 8).
/// </summary>
public sealed record PayerOutstandingDto(
    Guid PayerId,
    string PayerName,
    decimal Receivable,
    decimal UnappliedCash,
    decimal CreditAvailable,
    decimal NetOutstanding);
