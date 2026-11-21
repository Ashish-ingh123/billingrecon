using System.Net;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The Seq sink, exercised against an in-process stand-in for Seq (no Docker needed): what is posted to Seq carries
/// the correlation id and is already masked. A real Seq server is checked by hand (docs/runbook.md).
/// </summary>
public sealed class SeqSinkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Events_shipped_to_Seq_carry_the_correlation_id_and_are_masked()
    {
        await using var seq = await SeqStub.StartAsync();
        var correlationId = Guid.NewGuid();

        await using (var factory = new SeqApiFactory(seq.Url))
        {
            using var client = factory.CreateClientFor(FixedIdentity.Random());
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/probe/log", UriKind.Relative));
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId.ToString());

            using var response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Disposing the host flushes the sink; the wait only covers the last request still being in flight.
        var shipped = await seq.WaitForBodyAsync(
            bodies => bodies.Contains(correlationId.ToString(), StringComparison.Ordinal) && bodies.Contains("HTTP {RequestMethod}", StringComparison.Ordinal),
            Timeout);

        seq.Requests.ShouldAllBe(received => received.Method == "POST");

        var requestEvents = shipped.Split('\n').Where(line => line.Contains(correlationId.ToString(), StringComparison.Ordinal)).ToArray();
        requestEvents.Length.ShouldBeGreaterThanOrEqualTo(2);
        requestEvents.ShouldAllBe(line => line.Contains($"\"CorrelationId\":\"{correlationId}\"", StringComparison.Ordinal));
        requestEvents.ShouldContain(line => line.Contains("Probe contact", StringComparison.Ordinal));

        shipped.ShouldNotContain("priya.sharma");
        shipped.ShouldNotContain("9876543210");
    }

    [Fact]
    public async Task An_empty_Seq_url_switches_the_sink_off_even_when_another_source_configures_one()
    {
        await using var seq = await SeqStub.StartAsync();

        // A lower-priority source points at the stub, as appsettings.Development.json points at a real server;
        // the factory's own empty value must win, or the tests would ship their logs there.
        await using (var factory = new SeqConfiguredElsewhereApiFactory(seq.Url))
        {
            using var client = factory.CreateClientFor(FixedIdentity.Random());
            using var response = await client.GetAsync(new Uri("/api/v1/probe/log", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        seq.Requests.ShouldBeEmpty();
    }

    private sealed class SeqApiFactory(Uri seqUrl) : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Database:ConnectionString", UnreachableSqlServer),
            new("Seq:ServerUrl", seqUrl.AbsoluteUri),
        ];
    }

    private sealed class SeqConfiguredElsewhereApiFactory(Uri seqUrl) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Seq:ServerUrl", seqUrl.AbsoluteUri)]));

            // Added afterwards, so the factory's settings (an empty Seq:ServerUrl among them) take priority.
            base.ConfigureWebHost(builder);
        }
    }
}
