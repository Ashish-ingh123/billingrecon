using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Makes an inactive service item active again. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record ReactivateServiceItemCommand(ServiceItemId Id, string ExpectedVersion);
