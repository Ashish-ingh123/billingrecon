using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>The checker rejects a refund that waits, with a comment, under the version the client read.</summary>
public sealed record RejectRefundCommand(RefundId Id, string ExpectedVersion, string Comment);
