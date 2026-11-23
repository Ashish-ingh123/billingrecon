using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The dashboard (US-44, BR-902, ADR 0037): one endpoint per panel, so each loads and fails on its own. Its period is
/// <c>from</c> and <c>to</c>, business dates in India, both or neither (the last 30 days ending today), compared with the
/// previous period of the same length. Every figure ties to the ledger; the four lists are the reports of 6.1 and 6.2,
/// reused. For a TenantAdmin, a Supervisor or a Viewer.
/// </summary>
[Route(ApiRoutes.V1 + "/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>The six cards: billed, collected, outstanding dues, unapplied cash, pending approvals and drift, each against the previous period.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummaryAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IQueryHandler<DashboardSummaryQuery, DashboardSummaryDto> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new DashboardSummaryQuery { From = from, To = to }, ct));

    /// <summary>Billed against collected per day or per week, cumulative, and the daily figures of the cards' sparklines.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("trend")]
    [ProducesResponseType<DashboardTrendDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TrendAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] TrendInterval? interval,
        [FromServices] IQueryHandler<DashboardTrendQuery, DashboardTrendDto> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new DashboardTrendQuery { From = from, To = to, Interval = interval }, ct));

    /// <summary>What payers owe on posted bills by days past due, as of today in India.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("ageing")]
    [ProducesResponseType<AgeingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AgeingAsync(
        [FromServices] IQueryHandler<DashboardAgeingQuery, AgeingDto> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new DashboardAgeingQuery(), ct));

    /// <summary>The first five payers of the outstanding-by-payer report, the largest net outstanding first.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("top-payers")]
    [ProducesResponseType<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopPayersAsync(
        [FromServices] IQueryHandler<OutstandingByPayerQuery, ReportPage<PayerOutstandingDto, OutstandingTotalsDto>> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new OutstandingByPayerQuery { Page = Paging.FirstPage, PageSize = 5 }, ct));

    /// <summary>The five newest payments of the period, from the payment register.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("recent-payments")]
    [ProducesResponseType<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentPaymentsAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IQueryHandler<DashboardRecentPaymentsQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new DashboardRecentPaymentsQuery { From = from, To = to }, ct));

    /// <summary>The three oldest requests that wait for a checker, from the pending-approvals report.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("pending-approvals")]
    [ProducesResponseType<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingApprovalsAsync(
        [FromServices] IQueryHandler<PendingApprovalsQuery, ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new PendingApprovalsQuery { Page = Paging.FirstPage, PageSize = 3 }, ct));

    /// <summary>The first three drift rows of the reconciliation, with its totals; none when the caches equal the ledger.</summary>
    [Authorize(Policy = Policies.Dashboard)]
    [HttpGet("drift")]
    [ProducesResponseType<ReportPage<DriftRowDto, ReconciliationTotalsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DriftAsync(
        [FromServices] IQueryHandler<ReconciliationReportQuery, ReportPage<DriftRowDto, ReconciliationTotalsDto>> handler,
        CancellationToken ct)
        => Answer(await handler.HandleAsync(new ReconciliationReportQuery { Page = Paging.FirstPage, PageSize = 3 }, ct));

    private IActionResult Answer<T>(Domain.Common.Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
}
