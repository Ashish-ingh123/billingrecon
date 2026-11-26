using BillingRecon.Application.Payers;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// Registers the Infrastructure of the Payers module: its repositories and read services. The API's composition root
/// calls it next to <c>AddInfrastructure</c>, which may not name a business module (ADR 0003). The tables need no
/// registration: the context finds every entity configuration of this assembly.
/// </summary>
public static class PayersServiceCollectionExtensions
{
    /// <summary>Adds the Payers ports, scoped like the context they use.</summary>
    public static IServiceCollection AddPayers(this IServiceCollection services)
    {
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IPayerRepository, PayerRepository>();
        services.AddScoped<IPatientReadService, PatientReadService>();
        services.AddScoped<IPartySearch, PartySearch>();
        services.AddScoped<IPayerReadService, PayerReadService>();

        // What other modules ask the Payers module, built on the read services above.
        services.AddScoped<IBillableParties, BillableParties>();
        services.AddScoped<IActivePayers, BillableParties>();

        // What Payers answers when records of the system before are brought in (ADR 0053).
        services.AddScoped<IImportParties, ImportParties>();

        return services;
    }
}
