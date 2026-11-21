using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="ApplyCreditNoteCommand"/>: the version the client read, between one and
/// <see cref="ApplyCreditNoteCommand.MaxApplications"/> lines, no bill named twice, and for every line a bill and an
/// amount above zero that is exact to the paisa. The caps of BR-213 are business rules, checked by the two aggregates.
/// </summary>
public sealed class ApplyCreditNoteCommandValidator : AbstractValidator<ApplyCreditNoteCommand>
{
    public ApplyCreditNoteCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");

        RuleFor(command => command.Applications)
            .Must(applications => applications.Count >= 1)
            .WithMessage("At least one application is required.")
            .Must(applications => applications.Count <= ApplyCreditNoteCommand.MaxApplications)
            .WithMessage($"One request applies credit to at most {ApplyCreditNoteCommand.MaxApplications} bills.")
            .Must(NameEveryBillOnce)
            .WithMessage("A bill is named once in a request; add the amounts for the same bill together.");

        // Every line is checked, not only the lines before the first invalid one.
        RuleForEach(command => command.Applications)
            .Cascade(CascadeMode.Continue)
            .NotNull().WithMessage("An application cannot be null.")
            .ChildRules(application =>
            {
                application.RuleFor(line => line.BillId)
                    .Must(billId => billId.Value != Guid.Empty)
                    .WithMessage("The bill id is required.");

                application.RuleFor(line => line.Amount)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("The amount is required.")
                    .Must(amount => amount is { } value && value > 0m && value <= BillLine.MaxLineAmount && Money.TryOf(value, out _))
                    .WithMessage($"The amount is more than 0 and at most 9999999999.99, with at most {Money.Scale} decimals.");
            });
    }

    // Lines without a bill are refused by their own rule; here only the bills that are named count.
    private static bool NameEveryBillOnce(IReadOnlyList<CreditApplicationInput> applications)
    {
        var named = applications.Where(application => application is not null && application.BillId.Value != Guid.Empty).Select(application => application.BillId).ToArray();

        return named.Distinct().Count() == named.Length;
    }
}
