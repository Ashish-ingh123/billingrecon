using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BillingRecon.IntegrationTests.Support;

internal static class LogEventExtensions
{
    /// <summary>The value of a scalar property, or null when the event has no such property.</summary>
    public static object? Scalar(this LogEvent logEvent, string propertyName)
        => logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar ? scalar.Value : null;

    /// <summary>True for the one-event-per-request log written when a request completes.</summary>
    public static bool IsRequestCompletion(this LogEvent logEvent)
        => logEvent.MessageTemplate.Text.StartsWith("HTTP {RequestMethod}", StringComparison.Ordinal);

    /// <summary>The event as the console and Seq sinks serialise it.</summary>
    public static string ToJson(this LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new CompactJsonFormatter().Format(logEvent, writer);

        return writer.ToString();
    }
}
