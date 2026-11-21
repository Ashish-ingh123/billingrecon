using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="RequestPaymentReversalCommand"/>: the version and a reason.</summary>
public sealed class RequestPaymentReversalCommandValidator : AbstractValidator<RequestPaymentReversalCommand>
{
    public RequestPaymentReversalCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Reason).ReversalReason();
    }
}
