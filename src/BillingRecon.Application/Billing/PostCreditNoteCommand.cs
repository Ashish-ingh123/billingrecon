using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The maker asks for a draft credit note to be posted, which hands it to a second person (BR-212, BR-604).
/// <c>ExpectedVersion</c> is the version the client read (BR-804).
/// </summary>
public sealed record PostCreditNoteCommand(CreditNoteId Id, string ExpectedVersion);
