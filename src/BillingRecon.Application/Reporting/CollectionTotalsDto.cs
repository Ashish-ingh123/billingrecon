namespace BillingRecon.Application.Reporting;

/// <summary>
/// Every method of one business date together. <see cref="Net"/> equals the movement of the Cash account in the ledger on
/// that date: its debits less its credits (BR-901).
/// </summary>
public sealed record CollectionTotalsDto(int Payments, decimal Received, decimal Reversed, decimal Refunded, decimal Net);
