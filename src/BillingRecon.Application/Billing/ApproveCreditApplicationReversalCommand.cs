using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A checker approves the reversal of an application of credit, which reverses it (ADR 0028, BR-606).
/// <c>ExpectedVersion</c> is the version of the credit note the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApproveCreditApplicationReversalCommand(CreditNoteId Id, CreditNoteAllocationId ApplicationId, string ExpectedVersion, string? Comment);
