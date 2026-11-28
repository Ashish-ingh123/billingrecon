using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>A payment or refund no active match holds, as the matcher reads it (from Payments' <c>IMatchablePayments</c>).</summary>
public sealed record MatchCandidate(Guid Id, string Number, Guid PayerId, DateOnly Date, string Method, string? Reference, Money Amount);
