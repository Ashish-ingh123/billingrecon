using System.Reflection;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Deny by default (ADR 0031, point 7): every endpoint needs an authenticated user through the fallback policy, and the
/// way out of it is <c>[AllowAnonymous]</c>, which these tests pin to the sign-in, so a new anonymous action fails
/// until someone decides. They also keep the one step from credentials to a user (<see cref="ISignInScope"/>, point 5)
/// to the sign-in use case.
/// </summary>
public sealed class AuthenticationConventionTests
{
    private const BindingFlags Actions = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void The_sign_in_is_the_only_anonymous_action()
    {
        var anonymous = Assemblies.Api.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(Actions).Cast<MemberInfo>().Prepend(type))
            .Where(member => member.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any())
            .Select(member => member is Type type ? type.Name : $"{member.DeclaringType!.Name}.{member.Name}");

        anonymous.ShouldBe(["AuthController.LoginAsync"]);
    }

    [Fact]
    public void Only_the_sign_in_use_case_takes_the_step_into_a_user()
    {
        var takers = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ISignInScope))))
            .Select(type => type.FullName);

        takers.ShouldBe([typeof(SignInHandler).FullName]);

        var implementations = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && typeof(ISignInScope).IsAssignableFrom(type));

        implementations.ShouldBe([typeof(HttpSignInScope)]);
    }

    [Fact]
    public void Nothing_else_names_the_step_into_a_user()
    {
        // Not resolved from a service provider either: the port, its use case, its implementation and its registration.
        var naming = RepositoryFiles.Find("src", "*.cs")
            .Where(path => File.ReadAllText(RepositoryFiles.PathOf(path)).Contains(nameof(ISignInScope), StringComparison.Ordinal));

        naming.ShouldBe(
            [
                "src/BillingRecon.Api/Common/Authentication/AuthenticationServiceCollectionExtensions.cs",
                "src/BillingRecon.Api/Common/Authentication/HttpSignInScope.cs",
                "src/BillingRecon.Application/Identity/ISignInScope.cs",
                "src/BillingRecon.Application/Identity/SignInHandler.cs",
            ],
            ignoreOrder: true);
    }
}
