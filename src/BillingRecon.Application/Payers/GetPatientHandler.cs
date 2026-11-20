using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>Reads one patient. Another tenant's patient is not found, exactly like an id nobody has (BR-002).</summary>
public sealed class GetPatientHandler(IPatientReadService patients) : IQueryHandler<GetPatientQuery, PatientDto>
{
    /// <inheritdoc />
    public async Task<Result<PatientDto>> HandleAsync(GetPatientQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var patient = await patients.GetAsync(query.Id, ct);

        return patient is null ? PayersErrors.PatientNotFound() : patient;
    }
}
