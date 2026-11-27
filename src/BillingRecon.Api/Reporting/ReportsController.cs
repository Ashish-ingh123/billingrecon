using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The reports of R1 (BR-901; US-40, US-42; ADR 0036): the bill and payment registers, the collection of a day by method,
/// what payers owe and what is held for them, and the requests that wait for a checker. They only read, every role may
/// read them, and every figure ties to the ledger. A paged report answers its totals over every page with it. The
/// reconciliation (US-41) is here too; drift it finds is also written to the log as an error, since it is a defect of
/// the highest severity (CLAUDE.md section 4, rule 4).
/// </summary>
[Route(ApiRoutes.V1 + "/reports")]
public sealed class ReportsController(ILogger<ReportsController> logger, IAuthorizationService authorization) : ApiControllerBase
{
    /// <summary>
    /// The drift between the cached balances and the ledger, and every number series that is not gapless (US-41): an
    /// empty report when none. It never corrects anything.
    /// </summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("reconciliation")]
    [ProducesResponseType<ReportPage<DriftRowDto, ReconciliationTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReconciliationAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<ReconciliationReportQuery, ReportPage<DriftRowDto, ReconciliationTotalsDto>> handler,
        CancellationToken ct)
    {
        var query = new ReconciliationReportQuery { Page = page ?? Paging.FirstPage, PageSize = pageSize ?? Paging.DefaultPageSize };
        var result = await handler.HandleAsync(query, ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        if (result.Value.Totals.Rows > 0)
        {
            logger.LogError(
                "The reconciliation found {DriftRows} drift rows, broken identities {DriftIdentities}",
                result.Value.Totals.Rows,
                result.Value.Totals.ByIdentity.Select(identity => identity.Identity).ToArray());
        }

        return Ok(result.Value);
    }

    /// <summary>The posted bills of a period by bill date, the current month in India when none is given.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("bill-register")]
    [ProducesResponseType<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BillRegisterAsync(
        [FromQuery] BillRegisterRequest request,
        [FromServices] IQueryHandler<BillRegisterQuery, ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The payments of a period by payment date, reversed ones included, the current month in India when none is given.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("payment-register")]
    [ProducesResponseType<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PaymentRegisterAsync(
        [FromQuery] PaymentRegisterRequest request,
        [FromServices] IQueryHandler<PaymentRegisterQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The collection of a business date by method (US-42), today in India when no date is given.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("daily-collection")]
    [ProducesResponseType<DailyCollectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DailyCollectionAsync(
        [FromQuery] DateOnly? date,
        [FromServices] IQueryHandler<DailyCollectionQuery, DailyCollectionDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new DailyCollectionQuery { Date = date }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// The day-end report of a business date (US-48, BR-903), today in India when no date is given: every shift with its
    /// cash, and the cash outside the shifts. Its cash total equals the Cash row of the daily collection of the date.
    /// </summary>
    [Authorize(Policy = Policies.DayEnd)]
    [HttpGet("day-end")]
    [ProducesResponseType<DayEndDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DayEndAsync(
        [FromQuery] DateOnly? date,
        [FromServices] IQueryHandler<DayEndQuery, DayEndDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new DayEndQuery { Date = date }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The payers that owe something or have something held for them, the largest net outstanding first.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("outstanding-by-payer")]
    [ProducesResponseType<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OutstandingByPayerAsync(
        [FromQuery] OutstandingByPayerRequest request,
        [FromServices] IQueryHandler<OutstandingByPayerQuery, ReportPage<PayerOutstandingDto, OutstandingTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>One payer's outstanding, unapplied cash and available credit (US-40). 404 <c>payer_not_found</c> for another tenant's payer.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("payers/{payerId:guid}/balance")]
    [ProducesResponseType<PayerOutstandingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PayerBalanceAsync(
        Guid payerId,
        [FromServices] IQueryHandler<GetPayerBalanceQuery, PayerOutstandingDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPayerBalanceQuery(new PayerId(payerId)), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The payments that hold unapplied cash, the oldest first.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("unapplied-cash")]
    [ProducesResponseType<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnappliedCashAsync(
        [FromQuery] PayerDocumentsRequest request,
        [FromServices] IQueryHandler<UnappliedCashQuery, ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToUnappliedCashQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The posted credit notes that hold available credit, the oldest first.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("available-credit")]
    [ProducesResponseType<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AvailableCreditAsync(
        [FromQuery] PayerDocumentsRequest request,
        [FromServices] IQueryHandler<AvailableCreditQuery, ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToAvailableCreditQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// The maker-checker requests that wait for a decision, the oldest first, with how long each has waited: those of
    /// the kinds of document the caller's role may read, and all of them for who may decide.
    /// </summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("pending-approvals")]
    [ProducesResponseType<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PendingApprovalsAsync(
        [FromQuery] PendingApprovalsRequest request,
        [FromServices] IQueryHandler<PendingApprovalsQuery, ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A row names a document, its payer and its amount: the caller is shown the kinds its role may read (R8).
        var visible = await PendingApprovalPolicies.VisibleToAsync(authorization, User);
        var result = await handler.HandleAsync(request.ToQuery() with { VisibleDocumentTypes = visible }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
    /// <summary>
    /// What an accountant's export of a period would hold (ADR 0062): how many vouchers, what each ledger of the
    /// accountant's books receives, the control total beside what our ledger says moved, and the ledgers the hospital
    /// has not named yet. <c>from</c> and <c>to</c> are business dates, both required, at most 92 days.
    /// </summary>
    [Authorize(Policy = Policies.AccountingExport)]
    [HttpGet("accounting-export/summary")]
    [ProducesResponseType<AccountingExportSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AccountingExportSummaryAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IQueryHandler<AccountingExportSummaryQuery, AccountingExportSummaryDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new AccountingExportSummaryQuery { From = from, To = to }, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// The accountant's export of a period as the XML file Tally imports as vouchers (ADR 0062): one voucher for each
    /// posting of the ledger, balanced, numbered as our document. 422 <c>accounting_ledger_not_mapped</c> names the
    /// ledgers an administrator must name first. The file is an attachment and is never cached.
    /// </summary>
    [Authorize(Policy = Policies.AccountingExport)]
    [HttpGet("accounting-export")]
    [Produces(TallyVoucherWriter.ContentType, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AccountingExportAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IQueryHandler<AccountingExportQuery, AccountingExport> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new AccountingExportQuery { From = from, To = to }, ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var export = result.Value;
        Response.ContentType = TallyVoucherWriter.ContentType + "; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{TallyVoucherWriter.FileName(export.Summary.From, export.Summary.To)}\"";
        Response.Headers.CacheControl = "no-store";
        await TallyVoucherWriter.WriteAsync(Response.Body, export, ct);

        return new EmptyResult();
    }
}
