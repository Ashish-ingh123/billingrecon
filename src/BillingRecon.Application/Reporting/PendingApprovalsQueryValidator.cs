using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="PendingApprovalsQuery"/>: a page within the limits and a known document type.</summary>
public sealed class PendingApprovalsQueryValidator : AbstractValidator<PendingApprovalsQuery>
{
    public PendingApprovalsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Sort).OneOf(PendingApprovalsQuery.Sorts);

        RuleFor(query => query.DocumentType).IsInEnum().WithMessage("The document type is not one the system knows.");
    }
}
