using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The shape of <see cref="ListAdjustmentsQuery"/>: the paging limits, ids that are not the empty id, and a status and
/// a kind the enums define.
/// </summary>
public sealed class ListAdjustmentsQueryValidator : AbstractValidator<ListAdjustmentsQuery>
{
    public ListAdjustmentsQueryValidator()
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
        RuleFor(query => query.Kind).IsInEnum().WithMessage("The kind is Discount, WriteOff or Rounding.");
    }
}
