using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Lists the tenant's statement lines one page at a time.</summary>
public sealed class ListStatementLinesHandler(IValidator<ListStatementLinesQuery> validator, IStatementReadService statements)
    : IQueryHandler<ListStatementLinesQuery, PageOf<StatementLineDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<StatementLineDto>>> HandleAsync(ListStatementLinesQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await statements.ListLinesAsync(query, ct);
    }
}
