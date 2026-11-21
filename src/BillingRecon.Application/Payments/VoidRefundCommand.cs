using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Voids a draft refund, with the reason, under the version the client read.</summary>
public sealed record VoidRefundCommand(RefundId Id, string ExpectedVersion, string Reason);
