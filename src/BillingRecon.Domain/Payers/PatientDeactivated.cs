using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>A patient was deactivated (BR-102): no new bill may be raised for them.</summary>
public sealed record PatientDeactivated(PatientId PatientId, Mrn Mrn) : IDomainEvent;
