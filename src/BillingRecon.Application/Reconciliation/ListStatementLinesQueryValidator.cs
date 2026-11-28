using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="ListStatementLinesQuery"/>: the paging limits, values the enums define, a period that runs forwards and a sane search.</summary>
public sealed class ListStatementLinesQueryValidator : AbstractValidator<ListStatementLinesQuery>
{
    public ListStatementLinesQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Order).OneOf(ListStatementLinesQuery.Sorts);
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Unmatched, Matched or Excluded.");
        RuleFor(query => query.Direction).IsInEnum().WithMessage("The direction is In or Out.");
        RuleFor(query => query.Search).OptionalText(ListStatementLinesQuery.SearchMaxLength, "search");
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("The period ends on or after the day it starts.");
    }
}
