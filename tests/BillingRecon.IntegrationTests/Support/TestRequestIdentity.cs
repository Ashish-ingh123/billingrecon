using BillingRecon.Api.Common.Logging;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The tenant and user of the request being handled, read from the principal that <see cref="TestPrincipalStartupFilter"/>
/// builds from the test headers. In the database-backed API tests it stands in for the identity that Phase 5 resolves
/// from the validated token (BR-602); the API's own registration outside Development is <c>UnavailableIdentity</c>,
/// which throws. Asked outside a request, or for an anonymous request, this one throws as well, so nothing acts for a
/// tenant by accident.
/// </summary>
internal sealed class TestRequestIdentity(IHttpContextAccessor httpContextAccessor) : ITenantContext, ICurrentUser
{
    /// <inheritdoc />
    public TenantId? TenantId => new TenantId(Guid.Parse(Identity().TenantId));

    /// <inheritdoc />
    public UserId? UserId => new UserId(Guid.Parse(Identity().UserId));

    private (string TenantId, string UserId) Identity()
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("The test identity was asked for the tenant or the user outside a request.");

        var (tenantId, userId) = RequestIdentity.Read(principal);

        return tenantId is not null && userId is not null
            ? (tenantId, userId)
            : throw new InvalidOperationException(
                $"The request is anonymous: it carries no {TestPrincipalStartupFilter.TenantHeader} and {TestPrincipalStartupFilter.UserHeader} headers. Create the client with CreateClientFor.");
    }
}
