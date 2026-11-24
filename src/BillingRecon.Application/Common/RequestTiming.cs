namespace BillingRecon.Application.Common;

/// <summary>
/// One duration measured while a request ran, under a short name (ADR 0046): <c>number-wait</c>, how long the request
/// waited for the number series of its document; <c>number-hold</c>, how long it then held that series, until its
/// transaction ended.
/// </summary>
/// <param name="Name">The name of the measurement, a token of the <c>Server-Timing</c> header.</param>
/// <param name="Duration">The time it took.</param>
public sealed record RequestTiming(string Name, TimeSpan Duration);
