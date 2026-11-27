using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="ListCashierShiftsQuery"/>.</summary>
public sealed class ListCashierShiftsQueryValidator : AbstractValidator<ListCashierShiftsQuery>
{
    /// <summary>Builds the rules.</summary>
    public ListCashierShiftsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Open, PendingApproval or Closed.");
        RuleFor(query => query.CashierUserId)
            .Must(cashier => cashier is not { } given || given.Value != Guid.Empty)
            .WithMessage("The cashier is the id of a user.");
    }
}
