using BillingRecon.Application.Receivables;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// Registers the Infrastructure of the Receivables module: the ledger, the opening of a payer's balances and the
/// reconciliation. The API's composition root calls it next to <c>AddInfrastructure</c>, which may not name a
/// business module (ADR 0003). The host must also provide <c>ICorrelationContext</c>, which only it can.
/// </summary>
public static class ReceivablesServiceCollectionExtensions
{
    /// <summary>Adds the Receivables ports, scoped like the context they use.</summary>
    public static IServiceCollection AddReceivables(this IServiceCollection services)
    {
        services.AddScoped<IReceivablesLedger, ReceivablesLedger>();
        services.AddScoped<IPayerAccounts, PayerAccounts>();
        services.AddScoped<IReconciliationQuery, ReconciliationQuery>();
        services.AddScoped<ILedgerMovements, LedgerMovementsQuery>();
        services.AddScoped<IShiftCash, ShiftCashQuery>();

        // The opening batches (ADR 0053).
        services.AddScoped<IOpeningBatchRepository, OpeningBatchRepository>();
        services.AddScoped<IOpeningBatchReadService, OpeningBatchReadService>();

        // The reconciliation of every hospital each night, with an alert on drift (ADR 0056).
        services.AddScoped<NightlyReconciliation>();
        services.AddSingleton<NightlyReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<NightlyReconciler>());

        return services;
    }
}
