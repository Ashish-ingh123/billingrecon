using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Reads patients without tracking them: for queries, and for the checks a command makes before it changes anything.
/// Every read is filtered by the current tenant, so another tenant's patient is not there at all.
/// </summary>
public interface IPatientReadService
{
    /// <summary>The patient, or null when the tenant has no patient with that id.</summary>
    public Task<PatientDto?> GetAsync(PatientId id, CancellationToken ct);

    /// <summary>One page of the tenant's patients that match <paramref name="query"/>, by name, then by id.</summary>
    public Task<PageOf<PatientDto>> ListAsync(ListPatientsQuery query, CancellationToken ct);

    /// <summary>Whether the tenant has a patient with this MRN, whatever its status (BR-101).</summary>
    public Task<bool> MrnExistsAsync(Mrn mrn, CancellationToken ct);
}
