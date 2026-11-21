using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>The maker hands a draft refund over to a second person, under the version the client read.</summary>
public sealed record PostRefundCommand(RefundId Id, string ExpectedVersion);
