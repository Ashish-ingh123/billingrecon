using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="OutstandingByPayerQuery"/>: a page within the limits and a search of sensible length.</summary>
public sealed class OutstandingByPayerQueryValidator : AbstractValidator<OutstandingByPayerQuery>
{
    public OutstandingByPayerQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Sort).OneOf(OutstandingByPayerQuery.Sorts);

        RuleFor(query => query.Search).OptionalText(OutstandingByPayerQuery.SearchMaxLength, "search");
    }
}
