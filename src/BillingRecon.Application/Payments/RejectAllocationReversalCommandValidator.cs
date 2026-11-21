using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="RejectAllocationReversalCommand"/>: a rejection says why (BR-605).</summary>
public sealed class RejectAllocationReversalCommandValidator : AbstractValidator<RejectAllocationReversalCommand>
{
    public RejectAllocationReversalCommandValidator()
    {
        RuleFor(command => command.Comment).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
