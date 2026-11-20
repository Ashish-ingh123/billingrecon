using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of <see cref="UpdatePayerCommand"/>: the version the client read, and a valid name and contact details.</summary>
public sealed class UpdatePayerCommandValidator : AbstractValidator<UpdatePayerCommand>
{
    public UpdatePayerCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");

        Include(new PayerDetailsValidator());
    }
}
