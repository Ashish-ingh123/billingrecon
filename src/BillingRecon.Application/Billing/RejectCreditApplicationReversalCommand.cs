using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>A checker rejects the reversal of an application of credit, saying why; the application stays in force (BR-605).</summary>
public sealed record RejectCreditApplicationReversalCommand(CreditNoteId Id, CreditNoteAllocationId ApplicationId, string ExpectedVersion, string Comment);
