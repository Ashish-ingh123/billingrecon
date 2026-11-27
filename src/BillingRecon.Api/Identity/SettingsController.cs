using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The settings of the tenant (BR-607, US-03; ADR 0033): the approval threshold, the back-date window and the default
/// due days, and the default prefix of each document type. A TenantAdmin's alone. A change is audited and applies from
/// the next request; the prefixes change only through a prefix change, which a second person decides.
/// </summary>
[Route(ApiRoutes.V1 + "/settings")]
public sealed class SettingsController : ApiControllerBase
{
    /// <summary>Reads the settings, with the tenant's version as the ETag.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpGet]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromServices] IQueryHandler<GetTenantSettingsQuery, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetTenantSettingsQuery(), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Changes the threshold, the back-date window and the default due days. Needs If-Match.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPatch]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        UpdateSettingsRequest request,
        [FromServices] ICommandHandler<UpdateTenantSettingsCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
    /// <summary>
    /// What the hospital prints at the head and foot of its documents (ADR 0054), for anyone who is signed in: whoever
    /// may print a document needs it, and it says nothing a printed page does not.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("letterhead")]
    [ProducesResponseType<LetterheadDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLetterheadAsync(
        [FromServices] IQueryHandler<GetLetterheadQuery, LetterheadDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetLetterheadQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Sets the letterhead (ADR 0054): every part is sent, one left out is cleared. Needs If-Match with the version of the
    /// settings, which the letterhead shares; answers the settings with their new version.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("letterhead")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateLetterheadAsync(
        UpdateLetterheadRequest request,
        [FromServices] ICommandHandler<UpdateLetterheadCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// The hospital's logo (ADR 0059), for anyone who is signed in, as the letterhead is. 404 when it has none.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("letterhead/logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetLogoAsync(
        [FromServices] IQueryHandler<GetLetterheadImageQuery, LetterheadImageDto> handler,
        CancellationToken ct)
        => ReadImageAsync(LetterheadImageKind.Logo, handler, ct);

    /// <summary>
    /// The signatory's signature (ADR 0059), for anyone who is signed in, as the letterhead is. 404 when there is none.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("letterhead/signature")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetSignatureAsync(
        [FromServices] IQueryHandler<GetLetterheadImageQuery, LetterheadImageDto> handler,
        CancellationToken ct)
        => ReadImageAsync(LetterheadImageKind.Signature, handler, ct);

    /// <summary>
    /// Sets or replaces the logo (ADR 0059). The body is the file itself, a PNG, JPEG or WebP image of at most 256 KB and
    /// 2000 pixels a side, with its media type as the Content-Type. Needs If-Match with the version of the settings;
    /// answers the settings with their new version.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("letterhead/logo")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> SetLogoAsync(
        [FromServices] ICommandHandler<SetLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
        => SetImageAsync(LetterheadImageKind.Logo, handler, ct);

    /// <summary>
    /// Sets or replaces the signature (ADR 0059). The body is the file itself, a PNG, JPEG or WebP image of at most
    /// 128 KB and 2000 pixels a side, with its media type as the Content-Type. Needs If-Match with the version of the
    /// settings; answers the settings with their new version.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("letterhead/signature")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> SetSignatureAsync(
        [FromServices] ICommandHandler<SetLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
        => SetImageAsync(LetterheadImageKind.Signature, handler, ct);

    /// <summary>Removes the logo from the letterhead (ADR 0059). Needs If-Match; answers the settings.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost("letterhead/logo/remove")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> RemoveLogoAsync(
        [FromServices] ICommandHandler<RemoveLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
        => RemoveImageAsync(LetterheadImageKind.Logo, handler, ct);

    /// <summary>Removes the signature from the letterhead (ADR 0059). Needs If-Match; answers the settings.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost("letterhead/signature/remove")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> RemoveSignatureAsync(
        [FromServices] ICommandHandler<RemoveLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
        => RemoveImageAsync(LetterheadImageKind.Signature, handler, ct);

    /// <summary>
    /// How the hospital prints its bills, receipts and refund vouchers (ADR 0059), for anyone who is signed in, as the
    /// letterhead is: three answers, a kind with no saved settings answering the defaults.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("print")]
    [ProducesResponseType<IReadOnlyList<PrintSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrintSettingsAsync(
        [FromServices] IQueryHandler<GetPrintSettingsQuery, IReadOnlyList<PrintSettingsDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPrintSettingsQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Sets how one kind of document is printed (ADR 0059): Bill, Payment (its receipt) or Refund (its voucher). Every
    /// part is sent. Needs If-Match with the version read, which for settings never saved is the one the read answered.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("print/{documentType}")]
    [ProducesResponseType<PrintSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdatePrintSettingsAsync(
        PrintedDocumentType documentType,
        UpdatePrintSettingsRequest request,
        [FromServices] ICommandHandler<UpdatePrintSettingsCommand, PrintSettingsDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(documentType, version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// The names the hospital gives the ledgers of its accountant's books (ADR 0062), with their version as the
    /// <c>ETag</c>. A hospital that never saved any answers none, with the version of a row that is not there.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpGet("accounting-ledgers")]
    [ProducesResponseType<AccountingLedgersDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccountingLedgersAsync(
        [FromServices] IQueryHandler<GetAccountingLedgersQuery, AccountingLedgersDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetAccountingLedgersQuery(), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Saves the names of the accountant's ledgers under <c>If-Match</c> (ADR 0062). An empty name takes a name away;
    /// the export of a period that needs it is refused until it is set again.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("accounting-ledgers")]
    [ProducesResponseType<AccountingLedgersDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAccountingLedgersAsync(
        UpdateAccountingLedgersRequest request,
        [FromServices] ICommandHandler<UpdateAccountingLedgersCommand, AccountingLedgersDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    private async Task<IActionResult> ReadImageAsync(
        LetterheadImageKind kind,
        IQueryHandler<GetLetterheadImageQuery, LetterheadImageDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetLetterheadImageQuery(kind), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // The hash is the image's name: a client that holds it need not read the bytes again.
        Response.Headers.ETag = EntityTags.Format(result.Value.Sha256);

        return File(result.Value.Content, result.Value.ContentType);
    }

    private async Task<IActionResult> SetImageAsync(
        LetterheadImageKind kind,
        ICommandHandler<SetLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new SetLetterheadImageCommand
        {
            Kind = kind,
            Content = await ReadBodyAsync(TenantImage.MaxBytesOf(kind), ct),
            DeclaredContentType = MediaTypeHeaderValue.TryParse(Request.ContentType, out var declared) ? declared.MediaType.Value : null,
            ExpectedVersion = version,
        };
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    private async Task<IActionResult> RemoveImageAsync(
        LetterheadImageKind kind,
        ICommandHandler<RemoveLetterheadImageCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new RemoveLetterheadImageCommand { Kind = kind, ExpectedVersion = version }, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    // Reads the body up to one byte more than an image may have, and no further: the validator then refuses a file
    // that is too large by its length, and a body of any size costs the server no more than that.
    private async Task<byte[]> ReadBodyAsync(int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await Request.Body.ReadAsync(buffer.AsMemory(read), ct);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return buffer[..read];
    }
}
