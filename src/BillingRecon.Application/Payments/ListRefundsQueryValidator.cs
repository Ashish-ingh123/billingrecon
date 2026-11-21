using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The paging rules, ids that are not empty, and a status the enum defines.</summary>
public sealed class ListRefundsQueryValidator : AbstractValidator<ListRefundsQuery>
{
    /// <summary>Creates the validator.</summary>
    public ListRefundsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");
        RuleFor(query => query.PaymentId)
            .Must(paymentId => paymentId is null || paymentId.Value.Value != Guid.Empty)
            .WithMessage("The payment id is not the empty id.");
        RuleFor(query => query.CreditNoteId)
            .Must(creditNoteId => creditNoteId is null || creditNoteId.Value.Value != Guid.Empty)
            .WithMessage("The credit note id is not the empty id.");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Draft, PendingApproval, Posted or Voided.");
    }
}
