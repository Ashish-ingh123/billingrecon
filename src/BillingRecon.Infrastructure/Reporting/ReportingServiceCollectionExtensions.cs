using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// Registers the Infrastructure of the Reporting module: its query services, which only read (ADR 0036). The API's
/// composition root calls it next to <c>AddInfrastructure</c>, which may not name a business module (ADR 0003).
/// </summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>Adds the Reporting ports, scoped like the context they read.</summary>
    public static IServiceCollection AddReporting(this IServiceCollection services)
    {
        services.AddScoped<IRegisterReports, RegisterReports>();
        services.AddScoped<ICollectionReports, CollectionReports>();
        services.AddScoped<IBalanceReports, BalanceReports>();

        // Whether a document of any module can be printed (ADR 0054): a read, like every report.
        services.AddScoped<IPrintableDocuments, PrintableDocuments>();
        services.AddScoped<IApprovalReports, ApprovalReports>();
        services.AddScoped<IDashboardReports, DashboardReports>();

        return services;
    }
}
