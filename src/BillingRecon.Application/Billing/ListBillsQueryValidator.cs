using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="ListBillsQuery"/>: the paging limits, ids that are not the empty id, a status the enum
/// defines, and a range of dates whose start is not after its end.
/// </summary>
public sealed class ListBillsQueryValidator : AbstractValidator<ListBillsQuery>
{
    public ListBillsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Search).OptionalText(DocumentSearch.MaxLength, "search");

        RuleFor(query => query.PatientId)
            .Must(patientId => patientId is null || patientId.Value.Value != Guid.Empty)
            .WithMessage("The patient id is not the empty id.");

        RuleFor(query => query.PayerId)
            .Must(payerId => payerId is null || payerId.Value.Value != Guid.Empty)
            .WithMessage("The payer id is not the empty id.");

        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Draft or Voided.");

        RuleFor(query => query.BillDateTo)
            .Must((query, to) => to is null || query.BillDateFrom is null || query.BillDateFrom <= to)
            .WithMessage("The end of the bill date range is not before its start.");
    }
}
