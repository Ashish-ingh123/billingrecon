using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="UpdateSettlementChannelCommand"/>: the shared rules; which kind needs which part is the handler's, which knows the kind.</summary>
public sealed class UpdateSettlementChannelCommandValidator : AbstractValidator<UpdateSettlementChannelCommand>
{
    public UpdateSettlementChannelCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new SettlementChannelInputValidator());
    }
}
