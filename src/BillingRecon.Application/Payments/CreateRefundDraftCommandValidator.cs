using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of a new refund: one source, an amount, a method with its reference, and a reason.</summary>
public sealed class CreateRefundDraftCommandValidator : AbstractValidator<CreateRefundDraftCommand>
{
    /// <summary>Creates the validator.</summary>
    public CreateRefundDraftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command)
            .Must(command => (command.PaymentId is { } paymentId && paymentId.Value != Guid.Empty) ^ (command.CreditNoteId is { } creditNoteId && creditNoteId.Value != Guid.Empty))
            .WithName("source")
            .WithMessage("A refund comes from exactly one source: give paymentId or creditNoteId, not both (BR-307).");
        RuleFor(command => command.Amount).RefundAmount();
        RuleFor(command => command.Method).RefundMethod();
        RuleFor(command => command.Reference).RefundReference(command => command.Method);
        RuleFor(command => command.Reason).RefundReason();
    }
}
