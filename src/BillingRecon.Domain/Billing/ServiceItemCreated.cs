using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An entry was added to the service catalogue. It carries no id: the id is assigned when the item is added to the unit
/// of work, after the event was raised (ADR 0015, section 2).
/// </summary>
public sealed record ServiceItemCreated(ServiceItemCode Code, string Name, UnitPrice DefaultUnitPrice, Percent TaxRate) : IDomainEvent;
