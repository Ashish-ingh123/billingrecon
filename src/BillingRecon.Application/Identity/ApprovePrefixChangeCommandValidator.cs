using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of an approval of a prefix change: the version read, and a comment of at most 500 characters.</summary>
public sealed class ApprovePrefixChangeCommandValidator : AbstractValidator<ApprovePrefixChangeCommand>
{
    /// <summary>Creates the validator.</summary>
    public ApprovePrefixChangeCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
