using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// Finds the patients and guarantors a file names (ADR 0053), each list in one query through the context and its tenant
/// filter. The MRN column compares without regard to case, as <see cref="Mrn"/> does, so the answer is keyed the same way.
/// </summary>
public sealed class ImportParties(BillingReconDbContext context) : IImportParties
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, KnownPatient>> FindPatientsAsync(IReadOnlyCollection<string> mrns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mrns);

        var wanted = mrns.Where(mrn => Mrn.TryOf(mrn, out _)).Select(Mrn.Of).Distinct().ToList();
        var found = new Dictionary<string, KnownPatient>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return found;
        }

        var rows = await (
            from patient in context.Set<Patient>().AsNoTracking()
            where wanted.Contains(patient.Mrn)
            join payer in context.Set<Payer>().AsNoTracking() on (PatientId?)patient.Id equals payer.PatientId into linked
            from payer in linked.DefaultIfEmpty()
            select new
            {
                patient.Id,
                patient.Mrn,
                patient.FullName,
                patient.Status,
                PayerId = (PayerId?)payer.Id,
                PayerStatus = (PayerStatus?)payer.Status,
            }).ToListAsync(ct);

        foreach (var row in rows)
        {
            found[row.Mrn.Value] = new KnownPatient(row.Id, row.FullName, row.Status == PatientStatus.Active, row.PayerId, row.PayerStatus == PayerStatus.Active);
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, KnownGuarantor>> FindGuarantorsAsync(IReadOnlyCollection<string> references, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(references);

        var wanted = references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<string, KnownGuarantor>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await context.Set<Payer>()
            .AsNoTracking()
            .Where(payer => payer.LegacyReference != null && wanted.Contains(payer.LegacyReference))
            .Select(payer => new { payer.Id, payer.LegacyReference, payer.Name, payer.Status })
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.LegacyReference!, row => new KnownGuarantor(row.Id, row.Name, row.Status == PayerStatus.Active), StringComparer.OrdinalIgnoreCase);
    }
}
