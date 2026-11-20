using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Deactivates a service item. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record DeactivateServiceItemCommand(ServiceItemId Id, string ExpectedVersion);
