namespace BillingRecon.Api.Common.Diagnostics;

/// <summary>The measurements the API may show its clients, off by default (ADR 0046).</summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>Adds <see cref="DiagnosticsOptions"/>, bound and checked when the API starts.</summary>
    public static IServiceCollection AddDiagnostics(this IServiceCollection services)
    {
        services.AddOptions<DiagnosticsOptions>()
            .BindConfiguration(DiagnosticsOptions.SectionName)
            .ValidateOnStart();

        return services;
    }
}
