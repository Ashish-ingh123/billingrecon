namespace BillingRecon.Api.Common.Diagnostics;

/// <summary>
/// Measurements the API may show its clients (ADR 0046), from the <c>Diagnostics</c> section. All of them are off by
/// default; they are for a load test, never for a deployment (docs/runbook.md section 14, docs/security.md section 10).
/// Bound and checked when the API starts: a value that is not a boolean stops it.
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// When true, an answer to a request that took a document number carries <c>Server-Timing</c> with how long the
    /// request waited for the number series and how long it held it. False by default.
    /// </summary>
    public bool ServerTiming { get; set; }
}
