using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>The shape of <see cref="ListApprovalRequestsQuery"/>: a page within the limits, a known status and document type.</summary>
public sealed class ListApprovalRequestsQueryValidator : AbstractValidator<ListApprovalRequestsQuery>
{
    public ListApprovalRequestsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Pending, Approved or Rejected.");
        RuleFor(query => query.DocumentType).IsInEnum().WithMessage("The document type is not one the system knows.");
    }
}
