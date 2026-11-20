using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>Lists the tenant's patients one page at a time, by name.</summary>
public sealed class ListPatientsHandler(IValidator<ListPatientsQuery> validator, IPatientReadService patients)
    : IQueryHandler<ListPatientsQuery, PageOf<PatientDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<PatientDto>>> HandleAsync(ListPatientsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await patients.ListAsync(query, ct);
    }
}
