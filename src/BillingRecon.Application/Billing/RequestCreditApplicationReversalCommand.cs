using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A cashier asks for one application of a credit note to be reversed, saying why (ADR 0028). <c>ExpectedVersion</c>
/// is the version of the credit note the cashier read (BR-804).
/// </summary>
public sealed record RequestCreditApplicationReversalCommand(CreditNoteId Id, CreditNoteAllocationId ApplicationId, string ExpectedVersion, string Reason);
