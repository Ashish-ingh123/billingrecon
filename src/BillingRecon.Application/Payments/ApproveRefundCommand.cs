using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>The checker approves a refund that waits, which posts it, under the version the client read.</summary>
public sealed record ApproveRefundCommand(RefundId Id, string ExpectedVersion, string? Comment);
