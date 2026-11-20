using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>The paging rules every list query shares. The validator of a list query includes it.</summary>
internal sealed class PagedQueryValidator : AbstractValidator<IPagedQuery>
{
    public PagedQueryValidator()
    {
        RuleFor(query => query.Page)
            .InclusiveBetween(Paging.FirstPage, Paging.MaxPage)
            .WithMessage($"The page is a number from {Paging.FirstPage} to {Paging.MaxPage}.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, Paging.MaxPageSize)
            .WithMessage($"The page size is a number from 1 to {Paging.MaxPageSize}.");
    }
}
