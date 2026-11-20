using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>An inactive patient was made active again.</summary>
public sealed record PatientReactivated(PatientId PatientId, Mrn Mrn) : IDomainEvent;
