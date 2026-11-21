using System.Reflection;
using System.Text.RegularExpressions;
using BillingRecon.Api.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// The permissions as the code declares them (ADR 0032): every action of the API but the sign-in names exactly one
/// policy of the catalogue, on the action itself; every policy of the catalogue is named somewhere; and every decision
/// of an approval request is given the roles of the current user, so the checker's role is asked whatever the endpoint
/// (BR-603). What each policy admits is tested over HTTP by the matrix test of the integration tests.
/// </summary>
public sealed partial class PermissionConventionTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Type[] Controllers =
    [
        .. Assemblies.Api.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type)),
    ];

    private static readonly MethodInfo[] Actions =
    [
        .. Controllers.SelectMany(controller => controller.GetMethods(Declared))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any()),
    ];

    [Fact]
    public void Every_action_but_the_sign_in_names_exactly_one_policy_of_the_catalogue_and_nothing_else()
    {
        var wrong = Actions
            .Where(action => action.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Where(action => action.GetCustomAttributes<AuthorizeAttribute>().ToArray() is not [{ Policy: { } policy, Roles: null, AuthenticationSchemes: null }]
                || !Policies.All.Contains(policy))
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToArray();

        wrong.ShouldBeEmpty("an action names one policy of Policies, and no roles or schemes of its own");
        Actions.Length.ShouldBeGreaterThan(70);
    }

    [Fact]
    public void No_controller_names_a_policy_for_all_its_actions()
    {
        // Two attributes would combine as "both", and a reader would have to look in two places (ADR 0032, point 2).
        Controllers.Where(controller => controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()).ShouldBeEmpty();
    }

    [Fact]
    public void Every_policy_of_the_catalogue_is_named_by_an_action()
    {
        var named = Actions.SelectMany(action => action.GetCustomAttributes<AuthorizeAttribute>()).Select(attribute => attribute.Policy).ToHashSet();

        Policies.All.Where(policy => !named.Contains(policy)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_decision_of_an_approval_request_is_given_the_roles_of_the_current_user()
    {
        var calls = RepositoryFiles.Find("src/BillingRecon.Application", "*.cs")
            .SelectMany(path => Decision().Matches(File.ReadAllText(RepositoryFiles.PathOf(path))).Select(match => (Path: path, Call: match.Value)))
            .ToArray();

        // Seven documents, each approved and rejected.
        calls.Length.ShouldBe(14);
        calls.Where(call => !call.Call.Contains("(checker, currentUser.Roles,", StringComparison.Ordinal))
            .Select(call => $"{call.Path}: {call.Call}")
            .ShouldBeEmpty();
    }

    [GeneratedRegex(@"\.(Approve|Reject)\([^;]*;")]
    private static partial Regex Decision();
}
