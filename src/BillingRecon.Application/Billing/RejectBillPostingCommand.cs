using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A checker rejects a bill that waits for approval, saying why; the bill becomes a draft again (BR-605).
/// <c>ExpectedVersion</c> is the version the checker read (BR-804).
/// </summary>
public sealed record RejectBillPostingCommand(BillId Id, string ExpectedVersion, string Comment);
