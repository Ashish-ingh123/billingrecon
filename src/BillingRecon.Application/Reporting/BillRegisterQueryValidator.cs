using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="BillRegisterQuery"/>: a page within the limits, a period of the register, a known status, a payer id that is not empty.</summary>
public sealed class BillRegisterQueryValidator : AbstractValidator<BillRegisterQuery>
{
    public BillRegisterQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        this.AddPeriodRules(query => query.From, query => query.To);

        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");

        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Posted, PartiallyPaid or Settled.");
    }
}
