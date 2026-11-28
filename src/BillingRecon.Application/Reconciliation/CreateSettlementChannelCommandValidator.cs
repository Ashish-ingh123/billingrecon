using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The shape of <see cref="CreateSettlementChannelCommand"/>: a kind, the shared rules, and the parts of its kind only:
/// a bank sends a mapping and no gateway or bank channel; a gateway sends its gateway and its bank channel and no mapping.
/// </summary>
public sealed class CreateSettlementChannelCommandValidator : AbstractValidator<CreateSettlementChannelCommand>
{
    public CreateSettlementChannelCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Kind)
            .NotNull().WithMessage("The kind is required: Bank or Gateway.")
            .IsInEnum().WithMessage("The kind is Bank or Gateway.");
        Include(new SettlementChannelInputValidator());

        When(command => command.Kind == SettlementChannelKind.Bank, () =>
        {
            RuleFor(command => command.Mapping).NotNull().WithMessage("A bank channel needs the column mapping its statement is read with.");
            RuleFor(command => command.Gateway).Null().WithMessage("A bank channel has no gateway.");
            RuleFor(command => command.BankChannelId).Null().WithMessage("A bank channel settles into no other channel.");
        });

        When(command => command.Kind == SettlementChannelKind.Gateway, () =>
        {
            RuleFor(command => command.Gateway)
                .NotNull().WithMessage("A gateway channel names its gateway: Razorpay.")
                .IsInEnum().WithMessage("The gateway is Razorpay.");
            RuleFor(command => command.BankChannelId).NotNull().WithMessage("A gateway channel names the bank channel it settles into.");
            RuleFor(command => command.Mapping).Null().WithMessage("A gateway channel has no column mapping: its settlement report has a fixed form.");
        });
    }
}
