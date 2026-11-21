using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The tenant's users (US-01; BR-601): create, read, list, change, replace roles, disable, enable, and reset a password.
/// Users are reference data like patients and payers: their POSTs take no Idempotency-Key, because the store of keys
/// keeps a fingerprint of the body and a body with a password must never be fingerprinted (ADR 0030); the unique
/// address and If-Match protect them instead. Every response that returns one user carries its version as the ETag;
/// no response carries a password or its hash. Another tenant's user answers 404. Who may call these is decided in
/// Prompts 5.2 and 5.3 (TenantAdmin only).
/// </summary>
[Route(ApiRoutes.V1 + "/users")]
public sealed class UsersController : ApiControllerBase
{
    private const string GetUserRoute = "GetUser";

    /// <summary>Creates a user with a password and at least one role. 409 when another user of the tenant has the address.</summary>
    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        CreateUserRequest request,
        [FromServices] ICommandHandler<CreateUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetUserRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one user.</summary>
    [HttpGet("{id:guid}", Name = GetUserRoute)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetUserQuery, UserDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetUserQuery(new UserId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the users one page at a time, by e-mail address.</summary>
    [HttpGet]
    [ProducesResponseType<PageOf<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListUsersRequest request,
        [FromServices] IQueryHandler<ListUsersQuery, PageOf<UserDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Changes the e-mail address and the display name. Needs If-Match.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        [FromServices] ICommandHandler<UpdateUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new UserId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Replaces the roles of a user with at least one. 422 when it would take the role away from the tenant's last
    /// active administrator. Needs If-Match.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReplaceRolesAsync(
        Guid id,
        ReplaceUserRolesRequest request,
        [FromServices] ICommandHandler<ReplaceUserRolesCommand, UserDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new UserId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Disables a user (BR-601). 422 for the caller themselves, for the tenant's last active administrator, and for a
    /// user who is disabled already. Needs If-Match; no body.
    /// </summary>
    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DisableAsync(
        Guid id,
        [FromServices] ICommandHandler<DisableUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new DisableUserCommand(new UserId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Enables a disabled user. 422 for a user who is active already. Needs If-Match; no body.</summary>
    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> EnableAsync(
        Guid id,
        [FromServices] ICommandHandler<EnableUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new EnableUserCommand(new UserId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>An administrator sets a new password for a user. The answer is the user, without the password. Needs If-Match.</summary>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ResetPasswordAsync(
        Guid id,
        ResetUserPasswordRequest request,
        [FromServices] ICommandHandler<ResetUserPasswordCommand, UserDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new UserId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
