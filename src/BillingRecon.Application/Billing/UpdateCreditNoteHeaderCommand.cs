using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Replaces the date and the reason of a draft credit note. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record UpdateCreditNoteHeaderCommand(CreditNoteId Id, string ExpectedVersion, DateOnly? CreditNoteDate, string Reason);
