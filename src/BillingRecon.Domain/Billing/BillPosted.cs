using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A draft bill was posted (BR-206, US-21): it has its gapless number, the payer owes its total, and it can no longer
/// be edited or voided (BR-006).
/// </summary>
public sealed record BillPosted(BillId BillId, string DocumentNumber, PayerId PayerId, Money Total, DateOnly BillDate) : IDomainEvent, IHasDocumentNumber;
