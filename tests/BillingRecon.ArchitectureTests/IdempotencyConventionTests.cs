using System.Reflection;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Api.Payers;
using BillingRecon.Api.Payments;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Every POST that creates or posts a financial document carries <c>[Idempotent]</c> (CLAUDE.md section 4, rule 5;
/// BR-803; ADR 0006). The attribute is opt-in, so forgetting it would be silent: these tests make every controller
/// that has a POST declare which kind it is. A new controller with a POST is in neither list and fails until someone
/// decides.
/// </summary>
public sealed class IdempotencyConventionTests
{
    /// <summary>Controllers of financial documents: every POST action must carry the attribute.</summary>
    private static readonly Type[] FinancialControllers =
    [
        typeof(BillsController),
        typeof(CreditNotesController),
        typeof(PaymentsController),
    ];

    /// <summary>
    /// Controllers whose POSTs are not financial: reference data, protected by its unique indexes and by If-Match
    /// (docs/api.md, section 7).
    /// </summary>
    private static readonly Type[] ReferenceDataControllers =
    [
        typeof(PatientsController),
        typeof(PayersController),
        typeof(ServiceItemsController),
    ];

    private static readonly Type[] ControllersWithPosts =
    [
        .. Assemblies.Api.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ApiControllerBase).IsAssignableFrom(type))
            .Where(type => PostActions(type).Length > 0),
    ];

    [Fact]
    public void Every_controller_with_a_post_is_declared_financial_or_reference_data()
    {
        ControllersWithPosts.ShouldBe([.. FinancialControllers, .. ReferenceDataControllers], ignoreOrder: true);
        FinancialControllers.Intersect(ReferenceDataControllers).ShouldBeEmpty();
    }

    [Fact]
    public void Every_post_of_a_financial_controller_is_idempotent()
    {
        var unprotected = FinancialControllers
            .SelectMany(PostActions)
            .Where(action => action.GetCustomAttribute<IdempotentAttribute>() is null)
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToArray();

        unprotected.ShouldBeEmpty("a financial POST without [Idempotent] can run twice when a client retries");
        FinancialControllers.SelectMany(PostActions).ShouldNotBeEmpty();
    }

    [Fact]
    public void No_post_of_a_reference_data_controller_asks_for_a_key_it_does_not_need()
    {
        ReferenceDataControllers
            .SelectMany(PostActions)
            .Where(action => action.GetCustomAttribute<IdempotentAttribute>() is not null)
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_attribute_is_only_on_posts()
    {
        var misplaced = Assemblies.Api.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<IdempotentAttribute>() is not null && method.GetCustomAttribute<HttpPostAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToArray();

        // PUT and PATCH are protected by If-Match (BR-804): repeating one after it succeeded is refused as stale.
        misplaced.ShouldBeEmpty();
    }

    private static MethodInfo[] PostActions(Type controller)
        => [.. controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null)];
}
