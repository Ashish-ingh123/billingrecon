using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of a list of prefix changes: a page, and a status and a type the enums define.</summary>
public sealed class ListPrefixChangesQueryValidator : AbstractValidator<ListPrefixChangesQuery>
{
    /// <summary>Creates the validator.</summary>
    public ListPrefixChangesQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Pending, Approved or Rejected.");
        RuleFor(query => query.DocumentType).IsInEnum().WithMessage("The document type is Bill, CreditNote, Payment, Adjustment or Refund.");
    }
}
