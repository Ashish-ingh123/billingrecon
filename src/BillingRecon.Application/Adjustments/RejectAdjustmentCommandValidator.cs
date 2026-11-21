using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>The shape of <see cref="RejectAdjustmentCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectAdjustmentCommandValidator : AbstractValidator<RejectAdjustmentCommand>
{
    public RejectAdjustmentCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
