using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A checker rejects a credit note that waits for approval, saying why; it becomes a draft again (BR-605).
/// <c>ExpectedVersion</c> is the version the checker read (BR-804).
/// </summary>
public sealed record RejectCreditNoteCommand(CreditNoteId Id, string ExpectedVersion, string Comment);
