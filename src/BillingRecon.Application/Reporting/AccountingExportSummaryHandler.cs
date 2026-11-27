using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>Answers the control totals of an export, with the names that are missing, so the screen can say so before a file is asked for.</summary>
public sealed class AccountingExportSummaryHandler(AccountingExportBuilder builder) : IQueryHandler<AccountingExportSummaryQuery, AccountingExportSummaryDto>
{
    /// <inheritdoc />
    public async Task<Result<AccountingExportSummaryDto>> HandleAsync(AccountingExportSummaryQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var built = await builder.BuildAsync(query.From, query.To, ct);

        return built.IsSuccess ? built.Value.Summary : built.Error;
    }
}
