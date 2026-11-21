using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Replaces a draft refund as a whole, under the version the client read. The source cannot be changed.</summary>
public sealed record UpdateRefundCommand(
    RefundId Id,
    string ExpectedVersion,
    DateOnly? RefundDate,
    decimal? Amount,
    PaymentMethod? Method,
    string? Reference,
    string Reason);
