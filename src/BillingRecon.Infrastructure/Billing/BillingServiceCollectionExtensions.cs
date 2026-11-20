using BillingRecon.Application.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// Registers the Infrastructure of the Billing module: its repositories and read services. The API's composition root
/// calls it next to <c>AddInfrastructure</c>, which may not name a business module (ADR 0003). The tables need no
/// registration: the context finds every entity configuration of this assembly.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>Adds the Billing ports, scoped like the context they use.</summary>
    public static IServiceCollection AddBilling(this IServiceCollection services)
    {
        services.AddScoped<IServiceItemRepository, ServiceItemRepository>();
        services.AddScoped<IServiceItemReadService, ServiceItemReadService>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<IBillReadService, BillReadService>();

        // What Billing answers to the modules that settle bills (docs/domain-model.md, section 6).
        services.AddScoped<IBillSettlement, BillSettlement>();

        return services;
    }
}
