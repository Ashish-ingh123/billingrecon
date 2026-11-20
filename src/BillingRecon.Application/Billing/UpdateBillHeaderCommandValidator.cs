using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="UpdateBillHeaderCommand"/>: the version the client read, both dates, and texts of a bounded
/// length. Whether the dates are allowed today is BR-205, answered by the aggregate.
/// </summary>
public sealed class UpdateBillHeaderCommandValidator : AbstractValidator<UpdateBillHeaderCommand>
{
    public UpdateBillHeaderCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.BillDate).NotNull().WithMessage("The bill date is required.");
        RuleFor(command => command.DueDate).NotNull().WithMessage("The due date is required.");
        RuleFor(command => command.EncounterReference).EncounterReference();
        RuleFor(command => command.Notes).Notes();
    }
}
