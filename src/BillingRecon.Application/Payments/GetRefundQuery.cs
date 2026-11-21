using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one refund.</summary>
public sealed record GetRefundQuery(RefundId Id);
