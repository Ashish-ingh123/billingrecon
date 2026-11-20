using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of <see cref="ListPayersQuery"/>: a page within the limits, a search of sensible length, a known status, a patient id that is not empty.</summary>
public sealed class ListPayersQueryValidator : AbstractValidator<ListPayersQuery>
{
    public ListPayersQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        RuleFor(query => query.Search).OptionalText(ListPayersQuery.SearchMaxLength, "search");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Active or Inactive.");
        RuleFor(query => query.PatientId)
            .Must(patientId => patientId is null || patientId.Value.Value != Guid.Empty)
            .WithMessage("The patient id is not the empty id.");
    }
}
