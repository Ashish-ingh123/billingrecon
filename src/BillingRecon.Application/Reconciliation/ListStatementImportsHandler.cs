using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Lists the tenant's imports one page at a time, the latest first.</summary>
public sealed class ListStatementImportsHandler(IValidator<ListStatementImportsQuery> validator, IStatementReadService statements)
    : IQueryHandler<ListStatementImportsQuery, PageOf<StatementImportDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<StatementImportDto>>> HandleAsync(ListStatementImportsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await statements.ListImportsAsync(query, ct);
    }
}
