using System.Net;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The API on Kestrel itself, listening on a port of the loopback address, as in its container (ADR 0035): what the
/// in-process test server cannot show. No answer names the server; a request over plain HTTP is refused; the scheme a
/// proxy on the same machine forwards is believed, since the framework trusts the loopback addresses.
/// </summary>
public sealed class KestrelTransportTests
{
    [Fact]
    public async Task On_Kestrel_no_answer_names_the_server_and_plain_HTTP_is_refused_unless_a_local_proxy_ended_TLS()
    {
        using var factory = new KestrelApiFactory();
        factory.StartServer();
        using var client = new HttpClient { BaseAddress = factory.Address };

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var plain = await client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative));
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/info", UriKind.Relative));
        forwarded.Headers.Add("X-Forwarded-Proto", "https");
        forwarded.Headers.Add("X-Forwarded-For", "198.51.100.4");
        using var throughProxy = await client.SendAsync(forwarded);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        await plain.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "https_required");

        // Through the proxy the request gets as far as asking for a token.
        await throughProxy.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");

        foreach (var response in new[] { live, plain, throughProxy })
        {
            response.Headers.Server.ShouldBeEmpty();
        }
    }

    private sealed class KestrelApiFactory : ApiFactory
    {
        public KestrelApiFactory()
        {
            UseKestrel(0);
        }

        /// <summary>The address Kestrel listens on, once started.</summary>
        public Uri Address => new(Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First());

        // The host a client of the loopback address names.
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new(AllowedHostsSetting, $"{Host};127.0.0.1"),
        ];
    }
}
