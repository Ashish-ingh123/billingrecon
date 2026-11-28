using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="ListStatementImportsQuery"/>: the paging limits.</summary>
public sealed class ListStatementImportsQueryValidator : AbstractValidator<ListStatementImportsQuery>
{
    public ListStatementImportsQueryValidator() => Include(new PagedQueryValidator());
}
