using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="ApproveCreditApplicationReversalCommand"/>: a comment, if there is one, that fits its column.</summary>
public sealed class ApproveCreditApplicationReversalCommandValidator : AbstractValidator<ApproveCreditApplicationReversalCommand>
{
    public ApproveCreditApplicationReversalCommandValidator()
    {
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
