using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="ApproveBillPostingCommand"/>: a comment, if there is one, that fits its column.</summary>
public sealed class ApproveBillPostingCommandValidator : AbstractValidator<ApproveBillPostingCommand>
{
    public ApproveBillPostingCommandValidator()
    {
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
