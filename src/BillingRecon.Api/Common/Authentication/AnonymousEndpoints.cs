using Microsoft.AspNetCore.Authorization;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// The endpoints anyone may call (ADR 0031, point 7): the sign-in, the health endpoints and, in Development, the
/// OpenAPI document and the Scalar page, each marked anonymous where it is declared. An anonymous endpoint
/// authenticates nobody: the token a request to it carries is not read, and the development identity is not applied.
/// So a sign-in looks the tenant up by the code it names and nothing else, whatever token the client still sends.
/// </summary>
public static class AnonymousEndpoints
{
    /// <summary>True when the endpoint the request was routed to is marked anonymous.</summary>
    public static bool IsAnonymous(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
