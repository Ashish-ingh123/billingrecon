using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Receivables;

/// <summary>
/// The opening balances of a hospital, brought in once from the system before (ADR 0053; BR-603, BR-801, BR-401). A
/// TenantAdmin checks the files with a dry run and submits them; a second person, a Supervisor or TenantAdmin, approves
/// or rejects the batch, and the approval posts every opening receivable and advance in one transaction. The files are
/// never answered back.
/// </summary>
[Route(ApiRoutes.V1 + "/opening-batches")]
public sealed class OpeningBatchesController : ApiControllerBase
{
    private const string GetOpeningBatchRoute = "GetOpeningBatch";

    /// <summary>Submits the files for approval. Nothing is posted until a second person approves the batch.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<OpeningBatchDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitAsync(
        OpeningBatchFilesRequest request,
        [FromServices] ICommandHandler<SubmitOpeningBatchCommand, OpeningBatchDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetOpeningBatchRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>One opening batch, with its version as the ETag.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpGet("{id:guid}", Name = GetOpeningBatchRoute)]
    [ProducesResponseType<OpeningBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetOpeningBatchQuery, OpeningBatchDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetOpeningBatchQuery(new OpeningBatchId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>The opening batches of the hospital, newest first.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpGet]
    [ProducesResponseType<PageOf<OpeningBatchDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListOpeningBatchesRequest request,
        [FromServices] IQueryHandler<ListOpeningBatchesQuery, PageOf<OpeningBatchDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Approves the batch: its opening receivables and advances are posted in the same transaction.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<OpeningBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DecideOpeningBatchRequest? request,
        [FromServices] ICommandHandler<ApproveOpeningBatchCommand, OpeningBatchDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new DecideOpeningBatchRequest()).ToApproval(new OpeningBatchId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Rejects the batch, saying why: nothing of it is posted.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<OpeningBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        DecideOpeningBatchRequest request,
        [FromServices] ICommandHandler<RejectOpeningBatchCommand, OpeningBatchDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToRejection(new OpeningBatchId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
