using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>Lists the tenant's approval requests one page at a time, oldest first: the checker's inbox (BR-605).</summary>
public sealed class ListApprovalRequestsHandler(IValidator<ListApprovalRequestsQuery> validator, IApprovalRequestReadService requests)
    : IQueryHandler<ListApprovalRequestsQuery, PageOf<ApprovalRequestDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<ApprovalRequestDto>>> HandleAsync(ListApprovalRequestsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await requests.ListAsync(query, ct);
    }
}
