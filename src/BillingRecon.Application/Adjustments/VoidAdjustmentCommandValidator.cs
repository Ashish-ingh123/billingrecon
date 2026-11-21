using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>The shape of <see cref="VoidAdjustmentCommand"/>: the version the client read, and a reason.</summary>
public sealed class VoidAdjustmentCommandValidator : AbstractValidator<VoidAdjustmentCommand>
{
    public VoidAdjustmentCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.Reason).RequiredReason();
    }
}
