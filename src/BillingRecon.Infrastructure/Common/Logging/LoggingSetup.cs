using System.Globalization;
using BillingRecon.Infrastructure.Common.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BillingRecon.Infrastructure.Common.Logging;

/// <summary>
/// Builds the Serilog pipeline: structured JSON on the console, Seq when it is configured, and the PII mask in front
/// of both. The <c>Serilog</c> configuration section can tune minimum levels and add sinks or enrichers; it cannot
/// remove the sinks or the mask set here, and whatever it adds still receives masked events.
/// </summary>
internal static class LoggingSetup
{
    public static void Configure(IServiceProvider services, LoggerConfiguration configuration)
    {
        var seq = services.GetRequiredService<IOptions<SeqOptions>>().Value;

        // The name of the running host (the API's assembly name), so Infrastructure does not have to know who hosts it.
        var applicationName = services.GetRequiredService<IHostEnvironment>().ApplicationName;

        configuration
            .MinimumLevel.Information()
            // Request logging writes one event per request; the framework's own per-request chatter is not needed.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // EF Core logs every SQL statement at Information. Warnings and errors are enough outside a debugging session.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            // Last, so it also masks what the enrichers above added.
            .Enrich.With<PiiMaskingEnricher>()
            .WriteTo.Console(new CompactJsonFormatter());

        if (seq.ServerUrl is not null)
        {
            configuration.WriteTo.Seq(seq.ServerUrl.AbsoluteUri, apiKey: seq.ApiKey, formatProvider: CultureInfo.InvariantCulture);
        }
    }
}
