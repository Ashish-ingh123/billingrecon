using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>The shape of <see cref="ApproveAdjustmentCommand"/>: a comment, if there is one, that fits its column.</summary>
public sealed class ApproveAdjustmentCommandValidator : AbstractValidator<ApproveAdjustmentCommand>
{
    public ApproveAdjustmentCommandValidator()
    {
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
