using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>A rejection says why (BR-605).</summary>
public sealed class RejectRefundCommandValidator : AbstractValidator<RejectRefundCommand>
{
    /// <summary>Creates the validator.</summary>
    public RejectRefundCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
