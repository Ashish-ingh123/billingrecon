using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The name, category, price or tax rate of a service item was changed. Raised only when a value really changed.</summary>
public sealed record ServiceItemUpdated(ServiceItemId ServiceItemId, ServiceItemCode Code, string Name) : IDomainEvent;
