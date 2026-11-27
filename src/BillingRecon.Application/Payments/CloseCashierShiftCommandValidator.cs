using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="CloseCashierShiftCommand"/>. Whether a reason is needed depends on the ledger, so the use case asks.</summary>
public sealed class CloseCashierShiftCommandValidator : AbstractValidator<CloseCashierShiftCommand>
{
    /// <summary>Builds the rules.</summary>
    public CloseCashierShiftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();

        RuleFor(command => command.Count)
            .NotNull().WithMessage("The count is required: the pieces of each note and coin, an empty list for an empty drawer.")
            .Must(count => count is not null && count.All(line => line is { Denomination: not null, Pieces: not null }))
            .WithMessage("Each line of the count has a denomination and its pieces.")
            .Must(count => count is not null && CashCount.TryOf(count.Select(line => (line.Denomination ?? 0, line.Pieces ?? 0)), out _))
            .WithMessage($"The count names each of the denominations {string.Join(", ", CashCount.Denominations)} at most once, with 0 to {CashCount.MaxPieces} pieces.");

        RuleFor(command => command.LooseChange)
            .Must(amount => amount is not { } value || (value >= 0m && value < 1m && Money.TryOf(value, out _)))
            .WithMessage($"The loose change is what is below one rupee: 0 to 0.99, with at most {Money.Scale} decimals.");

        RuleFor(command => command.Reason).OptionalText(CashierShift.ReasonMaxLength, "reason");
    }
}
