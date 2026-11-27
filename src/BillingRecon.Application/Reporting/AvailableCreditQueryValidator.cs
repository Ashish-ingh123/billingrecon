using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="AvailableCreditQuery"/>: a page within the limits and a payer id that is not empty.</summary>
public sealed class AvailableCreditQueryValidator : AbstractValidator<AvailableCreditQuery>
{
    public AvailableCreditQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Sort).OneOf(AvailableCreditQuery.Sorts);

        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");
    }
}
