using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>Test-only endpoints that make the API produce each kind of error response and a log event with contact data.</summary>
[Route(ApiRoutes.V1 + "/probe")]
public sealed class ProbeController(ILogger<ProbeController> logger) : ApiControllerBase
{
    /// <summary>The message of the exception thrown by <see cref="Throw"/>. It must never reach a client.</summary>
    public const string SecretExceptionMessage = "probe-secret-5f1c-do-not-leak";

    /// <summary>The e-mail address logged by <see cref="Log"/>. It must never reach a sink unmasked.</summary>
    public const string Email = "priya.sharma@example.com";

    /// <summary>The phone number logged by <see cref="Log"/>. It must never reach a sink unmasked.</summary>
    public const string Phone = "+91 98765 43210";

    /// <summary>The message logged by <see cref="WaitAsync"/> once the request has reached the action.</summary>
    public const string WaitingMessage = "Probe is waiting for the client to give up";

    /// <summary>The message of the exception thrown by <see cref="Rejected"/>.</summary>
    public const string RejectedMessage = "Request body too large.";

    /// <summary>How many field errors <see cref="ManyFieldErrors"/> returns: more than MVC's default cap of 200.</summary>
    public const int ManyFieldErrorsCount = 250;

    [HttpGet("throw")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC only treats instance methods as actions.")]
    public IActionResult Throw() => throw new InvalidOperationException(SecretExceptionMessage);

    [HttpGet("errors/{kind}")]
    public IActionResult Fail(ErrorKind kind) => Problem(kind switch
    {
        ErrorKind.Validation => Error.Validation(
            "amount_invalid",
            "The amount is invalid.",
            new Dictionary<string, string[]> { ["amount"] = ["Must be greater than zero."] }),
        ErrorKind.NotFound => Error.NotFound("bill_not_found", "The bill does not exist."),
        ErrorKind.Unauthorized => Error.Unauthorized("invalid_credentials", "The credentials are not right."),
        ErrorKind.Forbidden => Error.Forbidden("checker_is_maker", "The checker must be a different user from the maker."),
        ErrorKind.Conflict => Error.Conflict("concurrency_conflict", "The bill was changed by someone else."),
        ErrorKind.BusinessRule => Error.BusinessRule("bill_already_posted", "The bill is already posted."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind."),
    });

    /// <summary>Throws what the server throws when it refuses to read a request (too large, malformed, too slow).</summary>
    [HttpGet("rejected/{statusCode:int}")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC only treats instance methods as actions.")]
    public IActionResult Rejected(int statusCode) => throw new BadHttpRequestException(RejectedMessage, statusCode);

    /// <summary>Answers with a bare status, as a filter or a later slice might.</summary>
    [HttpGet("status/{statusCode:int}")]
    public IActionResult Status(int statusCode) => StatusCode(statusCode);

    [HttpGet("many-field-errors")]
    public IActionResult ManyFieldErrors() => Problem(Error.Validation(
        "lines_invalid",
        "Several lines are invalid.",
        Enumerable.Range(1, ManyFieldErrorsCount).ToDictionary(line => $"lines[{line}].quantity", _ => new[] { "Must be greater than zero." })));

    [HttpPost("echo")]
    public IActionResult Echo([FromBody] EchoRequest request) => Ok(request.Amount);

    [HttpGet("validate")]
    public IActionResult Validate([FromQuery, Range(1, 5)] int quantity) => Ok(quantity);

    [HttpGet("missing")]
    public IActionResult Missing() => NotFound();

    /// <summary>Who the request acts for, as the Application ports see it, and the roles of the principal.</summary>
    [HttpGet("identity")]
    public IActionResult Identity([FromServices] ITenantContext tenantContext, [FromServices] ICurrentUser currentUser)
        => Ok(new IdentityResponse(
            tenantContext.TenantId?.Value,
            currentUser.UserId?.Value,
            [.. User.FindAll(TokenClaims.RoleClaim).Select(claim => claim.Value)]));

    /// <summary>Logs <see cref="WaitingMessage"/> and then waits until the client gives up.</summary>
    [HttpGet("wait")]
    public async Task<IActionResult> WaitAsync(CancellationToken ct)
    {
        logger.LogInformation(WaitingMessage);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);

        return NoContent();
    }

    [HttpGet("log")]
    public IActionResult Log()
    {
        logger.LogInformation(
            "Probe contact {Email} {Phone} {Note} {@Patient}",
            Email,
            Phone,
            $"call 9876543210 or write to {Email}",
            new { Name = "Priya", Mobile = 9876543210L, Guarantor = new { EmailAddress = Email } });

        return NoContent();
    }
}
