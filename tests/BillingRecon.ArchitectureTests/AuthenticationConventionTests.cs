using System.Reflection;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Deny by default (ADR 0031, point 7): every endpoint needs an authenticated user through the fallback policy, and the
/// way out of it is <c>[AllowAnonymous]</c>, which these tests pin to the sign-in and the forgotten password (ADR 0061,
/// amended), so a new anonymous action fails
/// until someone decides. They also keep the one step from credentials to a user (<see cref="ISignInScope"/>, point 5)
/// to the sign-in and the two use cases of a forgotten password.
/// </summary>
public sealed class AuthenticationConventionTests
{
    private const BindingFlags Actions = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void The_sign_in_and_the_forgotten_password_are_the_only_anonymous_actions()
    {
        var anonymous = Assemblies.Api.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(Actions).Cast<MemberInfo>().Prepend(type))
            .Where(member => member.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any())
            .Select(member => member is Type type ? type.Name : $"{member.DeclaringType!.Name}.{member.Name}");

        anonymous.ShouldBe(
            [
                "AuthController.ForgotPasswordAsync",
                "AuthController.LoginAsync",
                "AuthController.OptionsAsync",
                "AuthController.ResetPasswordAsync",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_sign_in_carries_both_of_its_rate_limits_and_the_forgotten_password_the_one_per_address()
    {
        // ADR 0035: the limit per client address is the policy of the rate limiter, the limit per account a filter; a
        // limit on another action, or one taken off the sign-in, fails until someone decides. The two actions of a
        // forgotten password (ADR 0061, amended) are open to anyone too and carry the limit per client address; their
        // limit per user is the count of links in the hour, which the user's row keeps.
        string[] Carrying<TAttribute>()
            where TAttribute : Attribute
            => [.. Assemblies.Api.GetTypes()
                .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
                .SelectMany(type => type.GetMethods(Actions).Cast<MemberInfo>().Prepend(type))
                .Where(member => member.GetCustomAttributes<TAttribute>(inherit: true).Any())
                .Select(member => member is Type type ? type.Name : $"{member.DeclaringType!.Name}.{member.Name}")];

        Carrying<EnableRateLimitingAttribute>().ShouldBe(
            ["AuthController.ForgotPasswordAsync", "AuthController.LoginAsync", "AuthController.ResetPasswordAsync"],
            ignoreOrder: true);
        Carrying<SignInThrottleAttribute>().ShouldBe(["AuthController.LoginAsync"]);
        Carrying<DisableRateLimitingAttribute>().ShouldBeEmpty();

        foreach (var action in new[] { nameof(AuthController.LoginAsync), nameof(AuthController.ForgotPasswordAsync), nameof(AuthController.ResetPasswordAsync) })
        {
            typeof(AuthController).GetMethod(action)!
                .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(SignInLimits.PolicyName);
        }
    }

    [Fact]
    public void Only_the_sign_in_and_the_forgotten_password_take_the_step_into_a_user()
    {
        var takers = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ISignInScope))))
            .Select(type => type.FullName);

        takers.ShouldBe(
            [typeof(SignInHandler).FullName, typeof(RequestPasswordResetHandler).FullName, typeof(ResetPasswordByLinkHandler).FullName],
            ignoreOrder: true);

        var implementations = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && typeof(ISignInScope).IsAssignableFrom(type));

        implementations.ShouldBe([typeof(HttpSignInScope)]);
    }

    [Fact]
    public void Nothing_else_names_the_step_into_a_user()
    {
        // Not resolved from a service provider either: the port, its use cases, its implementation and its registration.
        var naming = RepositoryFiles.Find("src", "*.cs")
            .Where(path => File.ReadAllText(RepositoryFiles.PathOf(path)).Contains(nameof(ISignInScope), StringComparison.Ordinal));

        naming.ShouldBe(
            [
                "src/BillingRecon.Api/Common/Authentication/AuthenticationServiceCollectionExtensions.cs",
                "src/BillingRecon.Api/Common/Authentication/HttpSignInScope.cs",
                "src/BillingRecon.Application/Identity/ISignInScope.cs",
                "src/BillingRecon.Application/Identity/RequestPasswordResetHandler.cs",
                "src/BillingRecon.Application/Identity/ResetPasswordByLinkHandler.cs",
                "src/BillingRecon.Application/Identity/SignInHandler.cs",
            ],
            ignoreOrder: true);
    }
}
