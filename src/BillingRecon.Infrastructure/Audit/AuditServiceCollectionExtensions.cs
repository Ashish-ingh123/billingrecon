using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Audit;

/// <summary>
/// Registers the Audit module: the writer and the read service of the audit trail, and the subscriber through which
/// the unit of work writes one record for every domain event (BR-701, ADR 0020). The API's composition root calls it
/// next to <c>AddInfrastructure</c>. The host must also provide <see cref="ICorrelationContext"/>, which only it can.
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>Adds the Audit ports and the audit subscriber, scoped like the context they use.</summary>
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditReadService, AuditReadService>();
        services.AddScoped<IDomainEventSubscriber, AuditSubscriber>();

        return services;
    }
}
