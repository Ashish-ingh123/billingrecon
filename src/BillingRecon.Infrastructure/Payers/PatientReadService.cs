using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// Reads patients without tracking them, filtered by the tenant of the request's context. A search compares a part
/// of the MRN or a part of the name (ADR 0018, amended 2026-11-25); the search is a parameter with the wildcards of LIKE
/// escaped, so a <c>%</c> or <c>_</c> in it matches itself. Both comparisons follow the case-insensitive collation of the database.
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
            // The MRN is a value object stored through a converter, which has no Contains to translate: the cast names
            // its column, and LIKE compares the stored text. The search text is data, never a pattern.
            var pattern = LikePattern.Containing(search);
            patients = patients.Where(patient =>
                EF.Functions.Like((string)(object)patient.Mrn, pattern, LikePattern.Escape)
                || EF.Functions.Like(patient.FullName, pattern, LikePattern.Escape));
        }

        var totalCount = await patients.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<Patient> ordered = query.Order switch
        {
            { Column: "mrn", Descending: var down } => patients.By(patient => (string)(object)patient.Mrn, down).Then(patient => patient.Id, down),
            { Column: "fullName", Descending: var down } => patients.By(patient => patient.FullName, down).Then(patient => patient.Id, down),
            { Column: "dateOfBirth", Descending: var down } => patients.By(patient => patient.DateOfBirth, down).Then(patient => patient.Id, down),
            null => patients
                .OrderBy(patient => patient.FullName)
                .ThenBy(patient => patient.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PatientDto>([.. page.Select(patient => patient.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public Task<bool> MrnExistsAsync(Mrn mrn, CancellationToken ct) => Patients().AnyAsync(patient => patient.Mrn == mrn, ct);

    private IQueryable<Patient> Patients() => context.Set<Patient>().AsNoTracking();
}
