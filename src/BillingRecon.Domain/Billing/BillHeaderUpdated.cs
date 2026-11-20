using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The dates or the texts of a draft bill were corrected. Raised only when a value really changed.</summary>
public sealed record BillHeaderUpdated(BillId BillId, DateOnly BillDate, DateOnly DueDate) : IDomainEvent;
