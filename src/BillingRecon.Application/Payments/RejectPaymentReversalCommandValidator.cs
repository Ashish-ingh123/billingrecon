using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="RejectPaymentReversalCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectPaymentReversalCommandValidator : AbstractValidator<RejectPaymentReversalCommand>
{
    public RejectPaymentReversalCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
