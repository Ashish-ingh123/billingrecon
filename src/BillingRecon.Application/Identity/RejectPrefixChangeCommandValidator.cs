using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of a rejection of a prefix change: the version read, and the reason, which is required (BR-605).</summary>
public sealed class RejectPrefixChangeCommandValidator : AbstractValidator<RejectPrefixChangeCommand>
{
    /// <summary>Creates the validator.</summary>
    public RejectPrefixChangeCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment!).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
