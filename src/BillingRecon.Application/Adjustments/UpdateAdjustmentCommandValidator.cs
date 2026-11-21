using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>The shape of <see cref="UpdateAdjustmentCommand"/>: the version the client read, the date, the kind, the amount and the reason.</summary>
public sealed class UpdateAdjustmentCommandValidator : AbstractValidator<UpdateAdjustmentCommand>
{
    public UpdateAdjustmentCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.AdjustmentDate).NotNull().WithMessage("The adjustment date is required.");
        RuleFor(command => command.Kind).RequiredKind();
        RuleFor(command => command.Amount).RequiredAmount();
        RuleFor(command => command.Reason).RequiredReason();
    }
}
