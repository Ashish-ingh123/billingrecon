namespace BillingRecon.Application.Payments;

/// <summary>One denomination of a count as it is sent: both members are required.</summary>
public sealed record CashCountInput(int? Denomination, int? Pieces);
