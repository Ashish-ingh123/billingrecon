using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Who drafted the refund, by name (ADR 0059).</summary>
public sealed record GetRefundRecorderQuery(RefundId Id);
