using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="ApproveCashierShiftClosingCommand"/>.</summary>
public sealed class ApproveCashierShiftClosingCommandValidator : AbstractValidator<ApproveCashierShiftClosingCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApproveCashierShiftClosingCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
