using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="ListServiceItemsQuery"/>: the paging limits, a search text of a sane length and a status the enum defines.</summary>
public sealed class ListServiceItemsQueryValidator : AbstractValidator<ListServiceItemsQuery>
{
    public ListServiceItemsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        RuleFor(query => query.Search).OptionalText(ListServiceItemsQuery.SearchMaxLength, "search");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Active or Inactive.");
    }
}
