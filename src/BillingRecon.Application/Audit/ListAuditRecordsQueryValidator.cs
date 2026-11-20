using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using FluentValidation;

namespace BillingRecon.Application.Audit;

/// <summary>The shape of <see cref="ListAuditRecordsQuery"/>: a page within the limits, and a record to show the trail of.</summary>
public sealed class ListAuditRecordsQueryValidator : AbstractValidator<ListAuditRecordsQuery>
{
    public ListAuditRecordsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new PagedQueryValidator());

        RuleFor(query => query.EntityType).RequiredText(AuditRecord.EntityTypeMaxLength, "entity type");
        RuleFor(query => query.EntityId).NotEmpty().WithMessage("The entity id is required.");
    }
}
