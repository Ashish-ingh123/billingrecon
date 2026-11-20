using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Loads and adds patients for the commands that change them (docs/domain-model.md, section 9). Reads are filtered by
/// the current tenant. There is no remove: a patient is deactivated, never deleted (BR-102).
/// </summary>
public interface IPatientRepository
{
    /// <summary>The patient, tracked for a change, or null when the tenant has no patient with that id.</summary>
    public Task<Patient?> GetAsync(PatientId id, CancellationToken ct);

    /// <summary>Adds a new patient to the unit of work; it is saved by the commit.</summary>
    public void Add(Patient patient);
}
