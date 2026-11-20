using BillingRecon.Application.Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The API registers every use case and validator of the Application layer by scanning it (ADR 0018). These tests ask
/// the API's own container for each, in a request scope, so a handler whose dependencies are not registered fails here
/// and not on the first request that needs it.
/// </summary>
public sealed class UseCaseRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Type[] Contracts = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IValidator<>)];

    private static readonly Type[] Registered =
    [
        .. typeof(Application.AssemblyMarker).Assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && Contracts.Contains(contract.GetGenericTypeDefinition()))
            .Distinct(),
    ];

    [Fact]
    public void Every_handler_and_validator_resolves_in_a_request_scope()
    {
        Registered.Length.ShouldBeGreaterThan(0);
        using var scope = factory.Services.CreateScope();

        foreach (var contract in Registered)
        {
            scope.ServiceProvider.GetService(contract).ShouldNotBeNull($"{contract} is not registered");
        }
    }

    [Fact]
    public void Every_contract_has_exactly_one_implementation()
    {
        using var scope = factory.Services.CreateScope();

        foreach (var contract in Registered)
        {
            scope.ServiceProvider.GetServices(contract).Count().ShouldBe(1, $"{contract} has more than one implementation");
        }
    }

    [Fact]
    public void The_unit_of_work_of_a_request_dispatches_to_the_audit_subscriber_and_knows_its_correlation_id()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetServices<IDomainEventSubscriber>().ShouldHaveSingleItem().ShouldBeOfType<Application.Audit.AuditSubscriber>();
        scope.ServiceProvider.GetService<IUnitOfWork>().ShouldNotBeNull();

        // No request is running here, so the scope has an id of its own: never empty, and the same every time (BR-703).
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        correlation.CorrelationId.ShouldNotBe(Guid.Empty);
        correlation.CorrelationId.ShouldBe(correlation.CorrelationId);
        using var other = factory.Services.CreateScope();
        other.ServiceProvider.GetRequiredService<ICorrelationContext>().CorrelationId.ShouldNotBe(correlation.CorrelationId);
    }
}
