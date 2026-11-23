using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Gives a request of the in-process test server the client address a real server would: the test server has none, and
/// the forwarded headers (which the API believes from known proxies only) and the sign-in's limit per address depend on
/// it (ADR 0035). A request that sends <see cref="HeaderName"/> comes from that address; the header is removed before the
/// API sees the request. Runs ahead of the API's whole pipeline.
/// </summary>
internal sealed class RemoteAddressStartupFilter : IStartupFilter
{
    /// <summary>The test-only request header that names the client address.</summary>
    public const string HeaderName = "X-Test-Remote-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.Remove(HeaderName, out var value))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(value.ToString());
            }

            await nextMiddleware(context);
        });

        next(app);
    };
}
