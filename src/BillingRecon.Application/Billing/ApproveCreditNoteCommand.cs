using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A checker approves a credit note that waits for approval, which posts it (US-24, BR-606). <c>ExpectedVersion</c> is
/// the version the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApproveCreditNoteCommand(CreditNoteId Id, string ExpectedVersion, string? Comment);
