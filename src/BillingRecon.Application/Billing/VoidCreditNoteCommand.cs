using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Voids a draft credit note, with the reason. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record VoidCreditNoteCommand(CreditNoteId Id, string ExpectedVersion, string Reason);
