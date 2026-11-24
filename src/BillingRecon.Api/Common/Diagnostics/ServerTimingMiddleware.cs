using System.Globalization;
using BillingRecon.Application.Common;
using Microsoft.Extensions.Options;

namespace BillingRecon.Api.Common.Diagnostics;

/// <summary>
/// Writes the durations of <see cref="IRequestTimings"/> into the W3C <c>Server-Timing</c> header of the answer, for
/// example <c>number-wait;dur=1.25, number-hold;dur=6.8</c> in milliseconds, when <c>Diagnostics:ServerTiming</c> is
/// on (ADR 0046). The load test reads it to report how long the number series were waited for and held
/// (docs/requirements.md section 5). Off, or with nothing measured, the answer has no such header.
/// <para>
/// Written when the response starts: after the commit for a request whose answer the idempotency filter holds back until
/// then, and after the unit of work's commit for any other, so the hold is complete by then.
/// </para>
/// </summary>
public sealed class ServerTimingMiddleware(RequestDelegate next, IOptions<DiagnosticsOptions> options)
{
    /// <summary>The name of the header.</summary>
    public const string HeaderName = "Server-Timing";

    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (options.Value.ServerTiming)
        {
            context.Response.OnStarting(AddHeader, context);
        }

        return next(context);
    }

    /// <summary>The header's value for <paramref name="entries"/>, or null when there are none.</summary>
    public static string? Format(IReadOnlyList<RequestTiming> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count == 0
            ? null
            : string.Join(", ", entries.Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Name};dur={entry.Duration.TotalMilliseconds:0.###}")));
    }

    private static Task AddHeader(object state)
    {
        var context = (HttpContext)state;

        if (Format(context.RequestServices.GetRequiredService<IRequestTimings>().Entries) is { } value)
        {
            context.Response.Headers[HeaderName] = value;
        }

        return Task.CompletedTask;
    }
}
