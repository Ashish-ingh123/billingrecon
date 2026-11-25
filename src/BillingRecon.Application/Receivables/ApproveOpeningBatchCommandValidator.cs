using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>The shape of an approval: the version read, and a comment that fits.</summary>
public sealed class ApproveOpeningBatchCommandValidator : AbstractValidator<ApproveOpeningBatchCommand>
{
    /// <summary>Creates the validator.</summary>
    public ApproveOpeningBatchCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
