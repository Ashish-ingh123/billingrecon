using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>An entry of the service catalogue was deactivated: it is no longer offered for new bill lines.</summary>
public sealed record ServiceItemDeactivated(ServiceItemId ServiceItemId, ServiceItemCode Code, string Name) : IDomainEvent;
