using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of the removal of an image of the letterhead (ADR 0059): a version, and one of the two kinds.</summary>
public sealed class RemoveLetterheadImageCommandValidator : AbstractValidator<RemoveLetterheadImageCommand>
{
    /// <summary>Creates the validator.</summary>
    public RemoveLetterheadImageCommandValidator()
    {
        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Kind).IsInEnum().WithMessage("The image is the logo or the signature.");
    }
}
