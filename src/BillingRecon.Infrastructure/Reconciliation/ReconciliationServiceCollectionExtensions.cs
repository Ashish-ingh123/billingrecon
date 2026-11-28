using BillingRecon.Application.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// Registers the Infrastructure of the Reconciliation module (R2, ADR 0065): its repositories and read services. The
/// API's composition root calls it next to the other modules. The tables need no registration: the context finds every
/// entity configuration of this assembly.
/// </summary>
public static class ReconciliationServiceCollectionExtensions
{
    /// <summary>Adds the Reconciliation ports, scoped like the context they use.</summary>
    public static IServiceCollection AddReconciliation(this IServiceCollection services)
    {
        services.AddScoped<ISettlementChannelRepository, SettlementChannelRepository>();
        services.AddScoped<ISettlementChannelReadService, SettlementChannelReadService>();
        services.AddScoped<IStatementRepository, StatementRepository>();
        services.AddScoped<IStatementReadService, StatementReadService>();

        return services;
    }
}
