using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// A test-only aggregate that has everything the conventions of the context act on: a typed id, a tenant, both
/// sets of audit columns, a row version, an enum (required and optional), a decimal, an optional business date and
/// an optional instant. It exists because the production model has no tenant-scoped entity yet.
/// </summary>
public sealed class Widget(string name) : AggregateRoot<WidgetId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    public string Name { get; private set; } = name;

    public WidgetKind Kind { get; private set; }

    public WidgetKind? PreviousKind { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly? ShippedDate { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public TenantId TenantId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public UserId? CreatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public UserId? UpdatedByUserId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = name;
}
