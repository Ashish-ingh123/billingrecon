using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of <see cref="UpdatePatientCommand"/>: the version the client read, and valid demographics.</summary>
public sealed class UpdatePatientCommandValidator : AbstractValidator<UpdatePatientCommand>
{
    public UpdatePatientCommandValidator(IClock clock)
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");

        Include(new PatientDetailsValidator(clock));
    }
}
