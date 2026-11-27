using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of <see cref="ListPatientsQuery"/>: a page within the limits, a search of sensible length, a known status.</summary>
public sealed class ListPatientsQueryValidator : AbstractValidator<ListPatientsQuery>
{
    public ListPatientsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Order).OneOf(ListPatientsQuery.Sorts);

        RuleFor(query => query.Search).OptionalText(ListPatientsQuery.SearchMaxLength, "search");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Active or Inactive.");
    }
}
