using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The shape of print settings (ADR 0059): a version, a kind of document, and a layout that kind may have. What a
/// layout may be is said once, by <see cref="PrintLayout.Problems"/>: margins and the stationery's space within their
/// limits, a roll for no bill, and the SAC column and the tax split never switched off for a bill.
/// </summary>
public sealed class UpdatePrintSettingsCommandValidator : AbstractValidator<UpdatePrintSettingsCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdatePrintSettingsCommandValidator()
    {
        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.DocumentType).IsInEnum().WithMessage("The document is Bill, Payment, Refund or ShiftClosing.");

        RuleFor(command => command.Layout).Custom((layout, context) =>
        {
            if (!Enum.IsDefined(context.InstanceToValidate.DocumentType))
            {
                return;
            }

            foreach (var (part, problem) in layout.Problems(context.InstanceToValidate.DocumentType))
            {
                context.AddFailure(part, problem);
            }
        });
    }
}
