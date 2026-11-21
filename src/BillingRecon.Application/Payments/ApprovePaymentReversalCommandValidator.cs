using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="ApprovePaymentReversalCommand"/>: a comment, if there is one, that fits its column.</summary>
public sealed class ApprovePaymentReversalCommandValidator : AbstractValidator<ApprovePaymentReversalCommand>
{
    public ApprovePaymentReversalCommandValidator()
    {
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
