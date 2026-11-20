using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>
/// A patient was registered (BR-101). It carries the MRN and no id: the id is assigned when the patient is added to
/// the unit of work, after the event was raised (ADR 0015, section 2).
/// </summary>
public sealed record PatientRegistered(Mrn Mrn) : IDomainEvent;
