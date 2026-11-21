using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="RejectCreditNoteCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectCreditNoteCommandValidator : AbstractValidator<RejectCreditNoteCommand>
{
    public RejectCreditNoteCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
