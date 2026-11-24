using System.Globalization;
using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Diagnostics;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Numbering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests.Diagnostics;

/// <summary>
/// The Server-Timing of the number series without a database (ADR 0046): what the timer measures, how the header is
/// written, that it is off unless switched on, and that a setting that is not a boolean stops the API. That a posting and
/// a recording really answer it is in <c>ServerTimingApiTests</c>, on SQL Server.
/// </summary>
public sealed class ServerTimingTests
{
    [Fact]
    public void The_timer_measures_nothing_until_a_number_is_taken_and_the_hold_once_its_transaction_ends()
    {
        var timer = new NumberSeriesTimer();
        timer.Entries.ShouldBeEmpty();

        // The end of a transaction that held no series measures nothing.
        timer.TransactionEnded();
        timer.Entries.ShouldBeEmpty();

        var askedAt = NumberSeriesTimer.Start();
        timer.Taken(askedAt);
        timer.Entries.ShouldHaveSingleItem().Name.ShouldBe(NumberSeriesTimer.WaitName);

        timer.TransactionEnded();
        var entries = timer.Entries;
        entries.Select(entry => entry.Name).ShouldBe([NumberSeriesTimer.WaitName, NumberSeriesTimer.HoldName]);
        entries.ShouldAllBe(entry => entry.Duration >= TimeSpan.Zero);

        // A second end, a rollback after the commit for instance, leaves the hold as it was.
        timer.TransactionEnded();
        timer.Entries[1].ShouldBe(entries[1]);
    }

    [Fact]
    public void The_header_names_each_measurement_with_its_milliseconds_whatever_the_culture_of_the_machine()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // A culture that writes 1,25 for one and a quarter.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            ServerTimingMiddleware.Format(
            [
                new RequestTiming(NumberSeriesTimer.WaitName, TimeSpan.FromMicroseconds(1250)),
                new RequestTiming(NumberSeriesTimer.HoldName, TimeSpan.FromMilliseconds(6.8)),
            ]).ShouldBe("number-wait;dur=1.25, number-hold;dur=6.8");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        ServerTimingMiddleware.Format([]).ShouldBeNull();
    }

    [Fact]
    public void Server_timing_is_off_by_default()
    {
        using var factory = new ApiFactory();

        factory.Services.GetRequiredService<IOptions<DiagnosticsOptions>>().Value.ServerTiming.ShouldBeFalse();
    }

    [Fact]
    public async Task Switched_on_an_answer_carries_what_was_measured_and_off_it_carries_nothing()
    {
        using var on = new TimedApiFactory("true");
        using var off = new TimedApiFactory("false");

        using var measured = await on.CreateClient().GetAsync(new Uri(HealthEndpoints.Live, UriKind.Relative));
        using var silent = await off.CreateClient().GetAsync(new Uri(HealthEndpoints.Live, UriKind.Relative));

        measured.Headers.GetValues(ServerTimingMiddleware.HeaderName).ShouldHaveSingleItem().ShouldBe("number-wait;dur=1.25, number-hold;dur=6.8");
        silent.Headers.Contains(ServerTimingMiddleware.HeaderName).ShouldBeFalse();
    }

    [Fact]
    public void The_api_refuses_to_start_when_the_setting_is_not_a_boolean()
    {
        using var factory = new TimedApiFactory("sometimes");

        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Diagnostics:ServerTiming");
    }

    // The API with Diagnostics:ServerTiming set to the given text, and measurements that are always the same.
    private sealed class TimedApiFactory(string serverTiming) : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new($"{DiagnosticsOptions.SectionName}:{nameof(DiagnosticsOptions.ServerTiming)}", serverTiming),
        ];

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);

            services.AddScoped<IRequestTimings, FixedTimings>();
        }
    }

    private sealed class FixedTimings : IRequestTimings
    {
        public IReadOnlyList<RequestTiming> Entries { get; } =
        [
            new(NumberSeriesTimer.WaitName, TimeSpan.FromMicroseconds(1250)),
            new(NumberSeriesTimer.HoldName, TimeSpan.FromMilliseconds(6.8)),
        ];
    }
}
