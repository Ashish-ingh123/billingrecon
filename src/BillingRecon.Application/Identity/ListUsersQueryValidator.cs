using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The paging rules, a status and a role the enums define, and an address filter of at most 256 characters.</summary>
public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    /// <summary>Creates the validator.</summary>
    public ListUsersQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Active or Disabled.");
        RuleFor(query => query.Role).IsInEnum().WithMessage("The role is TenantAdmin, Supervisor, BillingClerk, Cashier or Viewer.");
        RuleFor(query => query.Email)
            .Must(email => email is null || email.Trim().Length is > 0 and <= User.EmailMaxLength)
            .WithMessage($"The e-mail filter has 1 to {User.EmailMaxLength} characters.");
    }
}
