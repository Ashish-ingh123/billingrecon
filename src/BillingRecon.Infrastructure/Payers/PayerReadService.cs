using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// Reads payers without tracking them, filtered by the tenant of the request's context. A search compares a part of
/// the name; Entity Framework passes it as a parameter and escapes the wildcards of LIKE, so a <c>%</c> or <c>_</c> in
/// it matches itself. The comparison follows the case-insensitive collation of the database.
/// </summary>
public sealed class PayerReadService(BillingReconDbContext context) : IPayerReadService
{
    /// <inheritdoc />
    public async Task<PayerDto?> GetAsync(PayerId id, CancellationToken ct)
    {
        var payer = await Payers().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return payer?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<PayerDto>> ListAsync(ListPayersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payers = Payers();

        if (query.Status is { } status)
        {
            payers = payers.Where(payer => payer.Status == status);
        }

        if (query.PatientId is { } patientId)
        {
            payers = payers.Where(payer => payer.PatientId == patientId);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            payers = payers.Where(payer => payer.Name.Contains(search));
        }

        var totalCount = await payers.CountAsync(ct);
        var page = await payers
            .OrderBy(payer => payer.Name)
            .ThenBy(payer => payer.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PayerDto>([.. page.Select(payer => payer.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public Task<bool> ExistsForPatientAsync(PatientId patientId, CancellationToken ct)
        => Payers().AnyAsync(payer => payer.PatientId == patientId, ct);

    private IQueryable<Payer> Payers() => context.Set<Payer>().AsNoTracking();
}
