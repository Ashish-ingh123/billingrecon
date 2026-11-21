using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="RequestCreditApplicationReversalCommand"/>: the version, the application and a reason.</summary>
public sealed class RequestCreditApplicationReversalCommandValidator : AbstractValidator<RequestCreditApplicationReversalCommand>
{
    public RequestCreditApplicationReversalCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.ApplicationId)
            .Must(applicationId => applicationId.Value != Guid.Empty)
            .WithMessage("The application id is required.");
        RuleFor(command => command.Reason).RequiredText(CreditNote.ReasonMaxLength, "reason");
    }
}
