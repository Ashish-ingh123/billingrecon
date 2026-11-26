using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Receivables;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>Puts a <see cref="RaceGate"/> around one service of a test host; the product's service does the work.</summary>
public static class RaceGateServices
{
    /// <summary>Gates the taking of a number of one document type.</summary>
    /// <param name="services">The services of the test host.</param>
    /// <param name="gate">The gate of the test.</param>
    /// <param name="gated">The document type whose numbers are gated.</param>
    public static void GateNumbersOf(this IServiceCollection services, RaceGate gate, DocumentType gated)
    {
        services.AddScoped<NumberSeriesAllocator>();
        services.AddScoped<INumberSeries>(provider => new RaceGatedNumberSeries(provider.GetRequiredService<NumberSeriesAllocator>(), gate, gated));
    }

    /// <summary>Gates the ledger postings of some kinds.</summary>
    /// <param name="services">The services of the test host.</param>
    /// <param name="gate">The gate of the test.</param>
    /// <param name="gated">The kinds of posting that are gated.</param>
    public static void GatePostingsOf(this IServiceCollection services, RaceGate gate, params LedgerPostingKind[] gated)
    {
        services.AddScoped<ReceivablesLedger>();
        services.AddScoped<IReceivablesLedger>(provider => new RaceGatedLedger(provider.GetRequiredService<ReceivablesLedger>(), gate, gated));
    }

    /// <summary>Gates the commit of the unit of work.</summary>
    /// <param name="services">The services of the test host.</param>
    /// <param name="gate">The gate of the test.</param>
    public static void GateCommits(this IServiceCollection services, RaceGate gate)
    {
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(provider => new RaceGatedUnitOfWork(provider.GetRequiredService<UnitOfWork>(), gate));
    }
}
