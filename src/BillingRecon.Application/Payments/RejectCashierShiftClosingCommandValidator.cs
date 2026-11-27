using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="RejectCashierShiftClosingCommand"/>.</summary>
public sealed class RejectCashierShiftClosingCommandValidator : AbstractValidator<RejectCashierShiftClosingCommand>
{
    /// <summary>Builds the rules.</summary>
    public RejectCashierShiftClosingCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment!).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
