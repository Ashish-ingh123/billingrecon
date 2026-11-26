using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The print settings of the current tenant (ADR 0059): at most one row for each kind of document.</summary>
public interface IPrintSettingsRepository
{
    /// <summary>The settings of <paramref name="documentType"/>, tracked by the unit of work; null while the hospital has saved none.</summary>
    public Task<PrintSettings?> FindAsync(PrintedDocumentType documentType, CancellationToken ct);

    /// <summary>Every row the hospital has saved, not tracked.</summary>
    public Task<IReadOnlyList<PrintSettings>> ListAsync(CancellationToken ct);

    /// <summary>Adds new settings to the unit of work.</summary>
    public void Add(PrintSettings settings);
}
