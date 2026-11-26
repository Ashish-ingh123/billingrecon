using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The print settings of the tenant, through the context and its tenant filter (ADR 0059).</summary>
public sealed class PrintSettingsRepository(BillingReconDbContext context) : IPrintSettingsRepository
{
    /// <inheritdoc />
    public Task<PrintSettings?> FindAsync(PrintedDocumentType documentType, CancellationToken ct)
        => context.Set<PrintSettings>().SingleOrDefaultAsync(settings => settings.DocumentType == documentType, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintSettings>> ListAsync(CancellationToken ct)
        => await context.Set<PrintSettings>().AsNoTracking().ToListAsync(ct);

    /// <inheritdoc />
    public void Add(PrintSettings settings) => context.Set<PrintSettings>().Add(settings);
}
