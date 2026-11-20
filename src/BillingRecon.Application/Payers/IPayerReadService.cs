using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Reads payers without tracking them: for queries, and for the checks a command makes before it changes anything.
/// Every read is filtered by the current tenant, so another tenant's payer is not there at all.
/// </summary>
public interface IPayerReadService
{
    /// <summary>The payer, or null when the tenant has no payer with that id.</summary>
    public Task<PayerDto?> GetAsync(PayerId id, CancellationToken ct);

    /// <summary>One page of the tenant's payers that match <paramref name="query"/>, by name, then by id.</summary>
    public Task<PageOf<PayerDto>> ListAsync(ListPayersQuery query, CancellationToken ct);

    /// <summary>Whether a payer of the tenant is linked to the patient, whatever its status (BR-104).</summary>
    public Task<bool> ExistsForPatientAsync(PatientId patientId, CancellationToken ct);
}
