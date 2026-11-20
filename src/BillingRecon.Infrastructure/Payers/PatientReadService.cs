using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// Reads patients without tracking them, filtered by the tenant of the request's context. A search compares the whole
/// MRN, or a part of the name; Entity Framework passes the search as a parameter and escapes the wildcards of LIKE, so
/// a <c>%</c> or <c>_</c> in it matches itself. Both comparisons follow the case-insensitive collation of the database.
/// </summary>
public sealed class PatientReadService(BillingReconDbContext context) : IPatientReadService
{
    /// <inheritdoc />
    public async Task<PatientDto?> GetAsync(PatientId id, CancellationToken ct)
    {
        var patient = await Patients().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return patient?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<PatientDto>> ListAsync(ListPatientsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var patients = Patients();

        if (query.Status is { } status)
        {
            patients = patients.Where(patient => patient.Status == status);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            patients = Mrn.TryOf(search, out var mrn)
                ? patients.Where(patient => patient.Mrn == mrn || patient.FullName.Contains(search))
                : patients.Where(patient => patient.FullName.Contains(search));
        }

        var totalCount = await patients.CountAsync(ct);
        var page = await patients
            .OrderBy(patient => patient.FullName)
            .ThenBy(patient => patient.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PatientDto>([.. page.Select(patient => patient.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public Task<bool> MrnExistsAsync(Mrn mrn, CancellationToken ct) => Patients().AnyAsync(patient => patient.Mrn == mrn, ct);

    private IQueryable<Patient> Patients() => context.Set<Patient>().AsNoTracking();
}
