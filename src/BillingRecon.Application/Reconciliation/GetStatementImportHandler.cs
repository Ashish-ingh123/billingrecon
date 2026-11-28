using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads one import. Another tenant's import is not found, exactly like an id nobody has (BR-1024).</summary>
public sealed class GetStatementImportHandler(IStatementReadService statements) : IQueryHandler<GetStatementImportQuery, StatementImportDto>
{
    /// <inheritdoc />
    public async Task<Result<StatementImportDto>> HandleAsync(GetStatementImportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var import = await statements.GetImportAsync(query.Id, ct);
        return import is null ? ReconciliationErrors.StatementImportNotFound() : import;
    }
}
