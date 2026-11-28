using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The imports of bank statements (BR-1003, BR-1004; ADR 0065): import a file whose dry run was read, read one import,
/// list them. The import is an idempotent POST (BR-803): a retry with the same key answers the first import unchanged.
/// An import is never changed or removed. Another tenant's import answers 404 (BR-1024).
/// </summary>
[Route(ApiRoutes.V1 + "/statement-imports")]
public sealed class StatementImportsController : ApiControllerBase
{
    private const string GetStatementImportRoute = "GetStatementImport";

    /// <summary>
    /// Imports the file its dry run read: the lines the channel does not hold yet. 409 <c>statement_file_changed</c> for
    /// another file than the dry run's, 409 <c>statement_file_already_imported</c> for a file the channel took before.
    /// </summary>
    [Authorize(Policy = Policies.ImportStatements)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<StatementImportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportAsync(
        StatementFileRequest request,
        [FromServices] ICommandHandler<ImportStatementCommand, StatementImportDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);
        return result.IsSuccess
            ? CreatedAtRoute(GetStatementImportRoute, new { id = result.Value.Id }, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Reads one import.</summary>
    [Authorize(Policy = Policies.ReadReconciliation)]
    [HttpGet("{id:guid}", Name = GetStatementImportRoute)]
    [ProducesResponseType<StatementImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetStatementImportQuery, StatementImportDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetStatementImportQuery(new StatementImportId(id)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Lists the imports one page at a time, the latest first.</summary>
    [Authorize(Policy = Policies.ReadReconciliation)]
    [HttpGet]
    [ProducesResponseType<PageOf<StatementImportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListStatementImportsRequest request,
        [FromServices] IQueryHandler<ListStatementImportsQuery, PageOf<StatementImportDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
