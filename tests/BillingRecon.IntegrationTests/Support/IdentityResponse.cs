namespace BillingRecon.IntegrationTests.Support;

/// <summary>What the probe endpoint <c>GET /api/v1/probe/identity</c> answers: who the request acts for, as the Application ports see it.</summary>
/// <param name="TenantId">The tenant of the request, from <c>ITenantContext</c>.</param>
/// <param name="UserId">The user of the request, from <c>ICurrentUser</c>.</param>
/// <param name="Roles">The role claims of the principal.</param>
public sealed record IdentityResponse(Guid? TenantId, Guid? UserId, IReadOnlyList<string> Roles);
