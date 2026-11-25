using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of an import of patients and guarantors: the patients' file is there, and neither file is too large.</summary>
public sealed class ImportPartiesCommandValidator : AbstractValidator<ImportPartiesCommand>
{
    /// <summary>Creates the validator.</summary>
    public ImportPartiesCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.PatientsCsv)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("The patients' file is required.")
            .Must(text => text!.Length <= ImportPartiesCommand.MaxFileLength)
            .WithMessage($"The patients' file holds at most {ImportPartiesCommand.MaxFileLength} characters; split it.");
        RuleFor(command => command.GuarantorsCsv)
            .Must(text => text is null || text.Length <= ImportPartiesCommand.MaxFileLength)
            .WithMessage($"The guarantors' file holds at most {ImportPartiesCommand.MaxFileLength} characters; split it.");
    }
}
