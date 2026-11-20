using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The checker rejected a bill that waited for approval (BR-605): it is a draft again.</summary>
public sealed record BillReturnedToDraft(BillId BillId, string Reason) : IDomainEvent, IHasReason;
