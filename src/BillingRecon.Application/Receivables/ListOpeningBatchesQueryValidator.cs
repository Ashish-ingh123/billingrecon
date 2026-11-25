using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>The shape of a list of opening batches: a page, and a status the enum defines.</summary>
public sealed class ListOpeningBatchesQueryValidator : AbstractValidator<ListOpeningBatchesQuery>
{
    /// <summary>Creates the validator.</summary>
    public ListOpeningBatchesQueryValidator()
    {
        Include(new PagedQueryValidator());
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is PendingApproval, Approved or Rejected.");
    }
}
