using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A draft bill was created (US-20). It carries no bill id: the id is assigned when the bill is added to the unit of
/// work, after the event was raised (ADR 0015, section 2).
/// </summary>
public sealed record BillDrafted(PatientId PatientId, PayerId PayerId, DateOnly BillDate, Money Total) : IDomainEvent;
