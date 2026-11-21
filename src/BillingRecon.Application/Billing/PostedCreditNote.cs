using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>What <see cref="ICreditNoteSettlement.FindPostedAsync"/> answers about a posted credit note: no type of the Billing domain.</summary>
public sealed record PostedCreditNote(PayerId PayerId, DateOnly CreditNoteDate, string Number, Money AvailableAmount);
