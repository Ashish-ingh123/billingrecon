namespace BillingRecon.Application.Payments;

/// <summary>One denomination of a count: the value of the note or coin in rupees, and how many pieces.</summary>
public sealed record CashCountLineDto(int Denomination, int Pieces);
