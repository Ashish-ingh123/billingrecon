using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Api.Common;

/// <summary>
/// Registers the use cases of the Application layer by convention (ADR 0018): every public class of the Application
/// assembly that implements <see cref="ICommandHandler{TCommand,TResult}"/> or <see cref="IQueryHandler{TQuery,TResult}"/>,
/// under that interface, and every public FluentValidation validator, under <see cref="IValidator{T}"/> of the type it
/// validates. All are scoped, like the context and the unit of work they use. A new use case needs no line here; the
/// Application tests check that every command and query has exactly one handler and that every validator is used.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    private static readonly Type[] Contracts = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IValidator<>)];

    /// <summary>Adds every handler and validator of the Application assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var implementations = typeof(Application.AssemblyMarker).Assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var contracts = implementation.GetInterfaces()
                .Where(contract => contract.IsGenericType && Contracts.Contains(contract.GetGenericTypeDefinition()));

            foreach (var contract in contracts)
            {
                services.AddScoped(contract, implementation);
            }
        }

        return services;
    }
}
