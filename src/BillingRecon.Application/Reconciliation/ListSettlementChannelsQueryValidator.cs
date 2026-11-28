using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="ListSettlementChannelsQuery"/>: the paging limits and a kind and a status the enums define.</summary>
public sealed class ListSettlementChannelsQueryValidator : AbstractValidator<ListSettlementChannelsQuery>
{
    public ListSettlementChannelsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());
        RuleFor(query => query.Kind).IsInEnum().WithMessage("The kind is Bank or Gateway.");
        RuleFor(query => query.Status).IsInEnum().WithMessage("The status is Active or Inactive.");
    }
}
