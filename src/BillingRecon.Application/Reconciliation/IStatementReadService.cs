using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads imports and statement lines, untracked and filtered by the tenant (BR-1024).</summary>
public interface IStatementReadService
{
    /// <summary>The import, or null when the tenant has none with that id.</summary>
    public Task<StatementImportDto?> GetImportAsync(StatementImportId id, CancellationToken ct);

    /// <summary>A page of the tenant's imports, the latest first.</summary>
    public Task<PageOf<StatementImportDto>> ListImportsAsync(ListStatementImportsQuery query, CancellationToken ct);

    /// <summary>A page of the tenant's statement lines, by date.</summary>
    public Task<PageOf<StatementLineDto>> ListLinesAsync(ListStatementLinesQuery query, CancellationToken ct);
}
