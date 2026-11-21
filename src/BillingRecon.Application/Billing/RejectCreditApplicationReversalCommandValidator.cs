using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="RejectCreditApplicationReversalCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectCreditApplicationReversalCommandValidator : AbstractValidator<RejectCreditApplicationReversalCommand>
{
    public RejectCreditApplicationReversalCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
