using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of a prefix change: a document type, a prefix of 1 to 3 letters or digits, and an optional comment.</summary>
public sealed class RequestPrefixChangeCommandValidator : AbstractValidator<RequestPrefixChangeCommand>
{
    /// <summary>Creates the validator.</summary>
    public RequestPrefixChangeCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.DocumentType)
            .NotNull().WithMessage("The document type is required: Bill, CreditNote, Payment, Adjustment or Refund.")
            .IsInEnum().WithMessage("The document type is Bill, CreditNote, Payment, Adjustment or Refund.")
            .Must(type => type is { } given && Tenant.HasPrefixSetting(given)).WithMessage("The document type is Bill, CreditNote, Payment, Adjustment or Refund: the prefix of an opening balance is not a setting.");
        RuleFor(command => command.Prefix).Prefix();
        RuleFor(command => command.Comment).OptionalText(ApprovalRequest.CommentMaxLength, "comment");
    }
}
