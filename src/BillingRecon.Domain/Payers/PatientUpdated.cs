using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>The demographics of a patient were corrected. Raised only when a value really changed.</summary>
public sealed record PatientUpdated(PatientId PatientId, Mrn Mrn) : IDomainEvent;
