using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Makes the in-process test server behave like Kestrel in one respect: the raw request target carries the query
/// string. The test server leaves it empty, which would hide a request log that includes the query string.
/// </summary>
internal sealed class RawTargetStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var request = context.Features.GetRequiredFeature<IHttpRequestFeature>();
            request.RawTarget = context.Request.PathBase.Value + context.Request.Path.Value + context.Request.QueryString.Value;

            await nextMiddleware(context);
        });

        next(app);
    };
}
