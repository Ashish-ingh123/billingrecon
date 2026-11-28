using BillingRecon.Application.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The rules shared by creating and changing a channel: the name, the label of at most four digits, the window and, when
/// a mapping is sent, the mapping itself, asked of <see cref="ColumnMapping.Problem"/> so that the validator and the
/// channel can never disagree. Which kind needs which part is the command's own validator's.
/// </summary>
internal sealed class SettlementChannelInputValidator : AbstractValidator<ISettlementChannelInput>
{
    public SettlementChannelInputValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(input => input.Name).RequiredText(SettlementChannel.NameMaxLength, "name");
        RuleFor(input => input.AccountLabel)
            .Must(label => string.IsNullOrWhiteSpace(label) || SettlementChannel.IsAccountLabel(label.Trim()))
            .WithMessage("The label is the last one to four digits of the account, never the full number.");
        RuleFor(input => input.DateWindowDays)
            .InclusiveBetween(0, SettlementChannel.MaxDateWindowDays)
            .When(input => input.DateWindowDays.HasValue)
            .WithMessage($"The date window is 0 to {SettlementChannel.MaxDateWindowDays} days.");
        RuleFor(input => input.Mapping)
            .Must(mapping => ColumnMapping.Problem(mapping!.ToMapping()) is null)
            .When(input => input.Mapping is not null)
            .WithMessage(input => ColumnMapping.Problem(input.Mapping!.ToMapping()) ?? string.Empty);
    }
}
