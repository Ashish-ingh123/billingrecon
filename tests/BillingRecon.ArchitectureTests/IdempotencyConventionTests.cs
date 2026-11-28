using System.Reflection;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payers;
using BillingRecon.Api.Payments;
using BillingRecon.Api.Receivables;
using BillingRecon.Api.Reconciliation;
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
        typeof(CreditApplicationReversalsController),
        typeof(RefundsController),
        typeof(AdjustmentsController),
        typeof(PaymentsController),
        typeof(PaymentReversalsController),

        // Since ADR 0053: its approval posts every opening receivable and advance of a batch.
        typeof(OpeningBatchesController),

        // Since ADR 0065: an import stores the lines of a bank statement; a retry answers the first import (BR-1003).
        typeof(StatementImportsController),
    ];

    /// <summary>
    /// Controllers whose POSTs are not financial (ADR 0030 point 7, ADR 0031 point 11, ADR 0033): reference data and the
    /// prefix changes, protected by their unique indexes and by If-Match (docs/api.md, section 7), and the sign-in, which
    /// changes nothing a retry could repeat. The users and the sign-in are here for a second reason too: their bodies hold passwords, and a stored
    /// fingerprint of such a body would be a fast hash of the password.
    /// </summary>
    private static readonly Type[] NonFinancialControllers =
    [
        typeof(PatientsController),
        typeof(PayersController),
        typeof(ServiceItemsController),
        typeof(UsersController),
        typeof(AuthController),
        typeof(PrefixChangesController),

        // Since ADR 0053: the import of patients, which matches what is there and can be sent again, and the dry run of
        // an opening batch, which writes nothing.
        typeof(PartyImportsController),
        typeof(OpeningBatchChecksController),

        // Since ADR 0065: the settlement channels are set-up data under their unique name and If-Match, and the dry run of
        // a statement writes nothing.
        typeof(SettlementChannelsController),
        typeof(StatementDryRunsController),

        // Since ADR 0066: a matching run posts nothing, and a second at once is kept apart by the unique indexes.
        typeof(MatchingRunsController),

        // Since ADR 0059: removing an image of the letterhead, under If-Match; a second removal changes nothing.
        typeof(SettingsController),

        // Since ADR 0060: a shift has no number and posts nothing. A second opening is refused by the rule of one shift
        // at a time, and a closing or a decision sent again by the version of the shift.
        typeof(CashierShiftsController),
    ];

    private static readonly Type[] ControllersWithPosts =
    [
        .. Assemblies.Api.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ApiControllerBase).IsAssignableFrom(type))
            .Where(type => PostActions(type).Length > 0),
    ];

    [Fact]
    public void Every_controller_with_a_post_is_declared_financial_or_not()
    {
        ControllersWithPosts.ShouldBe([.. FinancialControllers, .. NonFinancialControllers], ignoreOrder: true);
        FinancialControllers.Intersect(NonFinancialControllers).ShouldBeEmpty();
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
    public void No_post_of_a_non_financial_controller_asks_for_a_key_it_does_not_need()
    {
        NonFinancialControllers
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
