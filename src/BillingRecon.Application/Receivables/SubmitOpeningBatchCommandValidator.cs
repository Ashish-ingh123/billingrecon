using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>The shape of a submission: a cut-over date, and a comment that fits.</summary>
public sealed class SubmitOpeningBatchCommandValidator : AbstractValidator<SubmitOpeningBatchCommand>
{
    /// <summary>Creates the validator.</summary>
    public SubmitOpeningBatchCommandValidator()
    {
        RuleFor(command => command.CutOverDate).CutOverDate();
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
