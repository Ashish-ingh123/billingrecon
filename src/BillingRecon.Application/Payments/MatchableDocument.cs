namespace BillingRecon.Application.Payments;

/// <summary>
/// A payment or refund as the matching of statements sees it (<see cref="IMatchablePayments"/>): its id and number, its
/// payer, its date, its method in words, its reference and its amount in rupees.
/// </summary>
public sealed record MatchableDocument(Guid Id, string Number, Guid PayerId, DateOnly Date, string Method, string? Reference, decimal Amount);
