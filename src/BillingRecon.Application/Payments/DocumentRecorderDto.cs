namespace BillingRecon.Application.Payments;

/// <summary>
/// Who recorded a payment or drafted a refund, by name, for the sheet that prints it (ADR 0059). <paramref name="Name"/>
/// is null when no user of the hospital did: an opening advance brought in by a batch, or the operator's work.
/// </summary>
public sealed record DocumentRecorderDto(string? Name);
