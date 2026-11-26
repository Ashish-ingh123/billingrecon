using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="ListCreditNotesQuery"/>: the paging limits, ids that are not the empty id, and a status the
/// enum defines.
/// </summary>
public sealed class ListCreditNotesQueryValidator : AbstractValidator<ListCreditNotesQuery>
{
    public ListCreditNotesQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Search).OptionalText(DocumentSearch.MaxLength, "search");

        RuleFor(query => query.BillId)
            .Must(billId => billId is null || billId.Value.Value != Guid.Empty)
            .WithMessage("The bill id is not the empty id.");
        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Draft, PendingApproval, Posted or Voided.");
    }
}
