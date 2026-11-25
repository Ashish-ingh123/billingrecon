using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>The shape of a rejection: the version read, and why.</summary>
public sealed class RejectOpeningBatchCommandValidator : AbstractValidator<RejectOpeningBatchCommand>
{
    /// <summary>Creates the validator.</summary>
    public RejectOpeningBatchCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Comment!).RequiredText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
