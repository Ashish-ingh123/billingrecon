using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A checker approves a bill that waits for approval, which posts it (US-22, BR-606). <c>ExpectedVersion</c> is the
/// version the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApproveBillPostingCommand(BillId Id, string ExpectedVersion, string? Comment);
