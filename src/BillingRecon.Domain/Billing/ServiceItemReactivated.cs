using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>An inactive entry of the service catalogue was made active again.</summary>
public sealed record ServiceItemReactivated(ServiceItemId ServiceItemId, ServiceItemCode Code, string Name) : IDomainEvent;
