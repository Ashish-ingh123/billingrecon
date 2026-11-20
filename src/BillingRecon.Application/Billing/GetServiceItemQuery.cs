using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one service item of the tenant.</summary>
public sealed record GetServiceItemQuery(ServiceItemId Id);
