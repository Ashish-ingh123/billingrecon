using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="PaymentRegisterQuery"/>: a page within the limits, a period of the register, known filter values, a payer id that is not empty.</summary>
public sealed class PaymentRegisterQueryValidator : AbstractValidator<PaymentRegisterQuery>
{
    public PaymentRegisterQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        this.AddPeriodRules(query => query.From, query => query.To);

        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");

        RuleFor(query => query.Method).IsInEnum().WithMessage("The method is Cash, Card, UPI, BankTransfer or Cheque.");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Posted, PendingReversal or Reversed.");
        RuleFor(query => query.Order).IsInEnum().WithMessage("The order is OldestFirst or NewestFirst.");
    }
}
