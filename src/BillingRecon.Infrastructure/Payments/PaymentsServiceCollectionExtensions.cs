using BillingRecon.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// Registers the Infrastructure of the Payments module: the repositories and read services of payments and refunds. The API's composition root
/// calls it next to <c>AddInfrastructure</c>, which may not name a business module (ADR 0003).
/// </summary>
public static class PaymentsServiceCollectionExtensions
{
    /// <summary>Adds the Payments ports, scoped like the context they use.</summary>
    public static IServiceCollection AddPayments(this IServiceCollection services)
    {
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentReadService, PaymentReadService>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IRefundReadService, RefundReadService>();

        return services;
    }
}
