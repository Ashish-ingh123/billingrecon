using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="RejectBillPostingCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectBillPostingCommandValidator : AbstractValidator<RejectBillPostingCommand>
{
    public RejectBillPostingCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
