using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>What <see cref="ICreditNoteSettlement.PayRefundAsync"/> answers: the credit note's number and what it has available now.</summary>
public sealed record SettledCreditNote(string Number, Money AvailableAmount);
