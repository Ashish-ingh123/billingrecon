using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Common.Approvals;

/// <summary>
/// The approval requests of the tenant (BR-605): read-only, the checker's inbox. A request is opened by the use case
/// of its document (a bill at or above the threshold is posted) and decided through the document
/// (<c>POST /api/v1/bills/{id}/approve</c> or <c>.../reject</c>), because the approval is the posting (BR-606).
/// </summary>
[Route(ApiRoutes.V1 + "/approval-requests")]
public sealed class ApprovalRequestsController : ApiControllerBase
{
    /// <summary>Lists the requests one page at a time, oldest first; the pending ones unless <c>status</c> says otherwise.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpGet]
    [ProducesResponseType<PageOf<ApprovalRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListApprovalRequestsRequest request,
        [FromServices] IQueryHandler<ListApprovalRequestsQuery, PageOf<ApprovalRequestDto>> handler,
        CancellationToken ct)
    {
        var query = request.ToQuery();
        if (query.IsFailure)
        {
            return Problem(query.Error);
        }

        var result = await handler.HandleAsync(query.Value, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
