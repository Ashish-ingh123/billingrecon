using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Identity;

/// <summary>
/// Changes of a document-number prefix (BR-802, BR-604; ADR 0033): a TenantAdmin asks for one, and a Supervisor or
/// TenantAdmin who is not the maker approves or rejects it. The approval gives the type the new prefix on the tenant
/// and on the series of the current fiscal year, so the next number carries it; numbers already issued keep theirs.
/// The checkers read the changes. Not financial: no Idempotency-Key; one change waits per type, and If-Match guards the
/// decisions.
/// </summary>
[Route(ApiRoutes.V1 + "/settings/prefix-changes")]
public sealed class PrefixChangesController : ApiControllerBase
{
    private const string GetPrefixChangeRoute = "GetPrefixChange";

    /// <summary>
    /// Asks for a new prefix of one document type; the change waits for a second person. 409 when a change of the type
    /// already waits or the prefix belongs to another type; 422 when the type has the prefix already.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost]
    [ProducesResponseType<PrefixChangeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RequestAsync(
        RequestPrefixChangeRequest request,
        [FromServices] ICommandHandler<RequestPrefixChangeCommand, PrefixChangeDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetPrefixChangeRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one prefix change.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpGet("{id:guid}", Name = GetPrefixChangeRoute)]
    [ProducesResponseType<PrefixChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetPrefixChangeQuery, PrefixChangeDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPrefixChangeQuery(new PrefixChangeId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the prefix changes one page at a time, the newest first.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpGet]
    [ProducesResponseType<PageOf<PrefixChangeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListPrefixChangesRequest request,
        [FromServices] IQueryHandler<ListPrefixChangesQuery, PageOf<PrefixChangeDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// A second person approves a change that waits, and the prefix changes (BR-603, BR-606). 403 for the maker or a
    /// user who is neither Supervisor nor TenantAdmin; 409 when the prefix was taken by another type meanwhile, or the
    /// series changed in the same instant. Needs If-Match.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<PrefixChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApprovePrefixChangeRequest? request,
        [FromServices] ICommandHandler<ApprovePrefixChangeCommand, PrefixChangeDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new ApprovePrefixChangeRequest()).ToCommand(new PrefixChangeId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>A second person rejects a change that waits, saying why (BR-605); no prefix changes. Needs If-Match.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<PrefixChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        RejectPrefixChangeRequest request,
        [FromServices] ICommandHandler<RejectPrefixChangeCommand, PrefixChangeDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new PrefixChangeId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
