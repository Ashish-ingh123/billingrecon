using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The shape of <see cref="RecordPaymentCommand"/> (BR-301): a payer, a known method, an amount above zero that is
/// exact to the paisa, and a reference for every method except cash. The date is a business rule and is checked by
/// the use case against today (BR-205).
/// </summary>
public sealed class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.PayerId)
            .Must(payerId => payerId.Value != Guid.Empty)
            .WithMessage("The payer id is required.");

        RuleFor(command => command.Method)
            .NotNull().WithMessage("The method is required: Cash, Card, UPI, BankTransfer or Cheque.")
            .IsInEnum().WithMessage("The method is Cash, Card, UPI, BankTransfer or Cheque.");

        RuleFor(command => command.Amount)
            .NotNull().WithMessage("The amount is required.")
            .Must(amount => amount is { } value && value > 0m && value <= Payment.MaxAmount && Money.TryOf(value, out _))
            .WithMessage($"The amount is more than 0 and at most 9999999999.99, with at most {Money.Scale} decimals.");

        RuleFor(command => command.Reference)
            .OptionalText(Payment.ReferenceMaxLength, "reference")
            .Must((command, reference) => command.Method is null or PaymentMethod.Cash || !string.IsNullOrWhiteSpace(reference))
            .WithMessage("The reference is required for every method except Cash: the UPI id, card authorisation, UTR or cheque number (BR-301).");

        RuleFor(command => command.Notes).OptionalText(Payment.NotesMaxLength, "notes");
    }
}
