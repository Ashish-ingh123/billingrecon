using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="ListPaymentsQuery"/>: a page within the limits, known filter values, a date range that does not end before it starts.</summary>
public sealed class ListPaymentsQueryValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Search).OptionalText(DocumentSearch.MaxLength, "search");

        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");

        RuleFor(query => query.Method).IsInEnum().WithMessage("The method is Cash, Card, UPI, BankTransfer or Cheque.");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Posted.");

        RuleFor(query => query.PaymentDateTo)
            .Must((query, to) => to is null || query.PaymentDateFrom is null || query.PaymentDateFrom <= to)
            .WithMessage("The end of the payment date range is not before its start.");
    }
}
