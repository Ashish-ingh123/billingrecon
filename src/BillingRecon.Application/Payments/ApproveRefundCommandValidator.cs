using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The comment of an approval is optional.</summary>
public sealed class ApproveRefundCommandValidator : AbstractValidator<ApproveRefundCommand>
{
    /// <summary>Creates the validator.</summary>
    public ApproveRefundCommandValidator()
    {
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
