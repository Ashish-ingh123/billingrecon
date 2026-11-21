using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one credit note with its lines and applications.</summary>
public sealed record GetCreditNoteQuery(CreditNoteId Id);
