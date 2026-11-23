using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The maker-checker requests that wait for a decision, the oldest first, with how long each has waited (BR-901).</summary>
public sealed class PendingApprovalsHandler(IValidator<PendingApprovalsQuery> validator, IApprovalReports reports, IClock clock)
    : IQueryHandler<PendingApprovalsQuery, ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>> HandleAsync(PendingApprovalsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.PendingApprovalsAsync(query, clock.TodayIst, ct);
    }
}
