using BillingRecon.Application.Adjustments;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>
/// Registers the Infrastructure of the Adjustments module: its repository and read service. The API's composition
/// root calls it next to <c>AddInfrastructure</c>, which may not name a business module (ADR 0003).
/// </summary>
public static class AdjustmentsServiceCollectionExtensions
{
    /// <summary>Adds the Adjustments ports, scoped like the context they use.</summary>
    public static IServiceCollection AddAdjustments(this IServiceCollection services)
    {
        services.AddScoped<IAdjustmentRepository, AdjustmentRepository>();
        services.AddScoped<IAdjustmentReadService, AdjustmentReadService>();

        return services;
    }
}
