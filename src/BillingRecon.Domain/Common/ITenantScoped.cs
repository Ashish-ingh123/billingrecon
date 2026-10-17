namespace BillingRecon.Domain.Common;

/// <summary>
/// An entity that belongs to one tenant (BR-002). The persistence layer filters every query on such an entity by the
/// current tenant, sets <see cref="TenantId"/> when the entity is added and refuses to save it for any other tenant
/// (ADR 0009, ADR 0015). Application code never assigns it. The implementing class declares the property under the
/// same name, with a non-public setter.
/// </summary>
public interface ITenantScoped
{
    /// <summary>The owning tenant. Empty until the entity is added to the unit of work.</summary>
    public TenantId TenantId { get; }
}
