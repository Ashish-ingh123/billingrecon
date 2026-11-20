using System.Text.RegularExpressions;
using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Conventions of CLAUDE.md sections 2 and 5 that the build cannot check by itself: the banned packages stay out of
/// every project, controllers have one shape and reach the database only through Application use cases, and the API
/// has no minimal-API endpoints.
/// </summary>
public sealed class ConventionTests
{
    private static readonly string[] BannedPackages = ["MediatR", "AutoMapper", "FluentAssertions", "Microsoft.EntityFrameworkCore.InMemory"];

    public static TheoryData<string> BannedPackageNames()
    {
        var data = new TheoryData<string>();
        foreach (var package in BannedPackages)
        {
            data.Add(package);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BannedPackageNames))]
    public void Banned_packages_are_not_referenced_by_the_production_assemblies(string assemblyName)
    {
        foreach (var assembly in Assemblies.All)
        {
            assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ShouldNotContain(assemblyName, $"{assembly.GetName().Name} references {assemblyName}");
        }
    }

    [Theory]
    [MemberData(nameof(BannedPackageNames))]
    public void No_project_or_package_file_names_a_banned_package(string package)
    {
        // The assembly test sees production code only, and only the references the compiler kept. The test projects
        // are where the in-memory provider would creep in, so the project and package files of the whole repository
        // are read: a banned package cannot even get a central version.
        var files = RepositoryFiles.Find("src", "*.csproj")
            .Concat(RepositoryFiles.Find("tests", "*.csproj"))
            .Concat(RepositoryFiles.Find("src", "*.props"))
            .Concat(RepositoryFiles.Find("tests", "*.props"))
            .Append("Directory.Packages.props")
            .Append("Directory.Build.props");
        var reference = new Regex($@"Include=""{Regex.Escape(package)}(\.|"")", RegexOptions.IgnoreCase);

        foreach (var file in files)
        {
            reference.IsMatch(File.ReadAllText(RepositoryFiles.PathOf(file))).ShouldBeFalse($"{file} names the banned package {package}");
        }
    }

    [Fact]
    public void Controllers_are_sealed_derive_from_ApiControllerBase_and_end_with_Controller()
    {
        Types.InAssembly(Assemblies.Api)
            .That().Inherit(typeof(ControllerBase)).And().AreNotAbstract()
            .Should().BeSealed().And().HaveNameEndingWith("Controller").And().Inherit(typeof(ApiControllerBase))
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void Controllers_do_not_use_the_database_context_or_Infrastructure()
    {
        // A controller calls Application use cases. Infrastructure is reached by Program.cs alone, for registration.
        Types.InAssembly(Assemblies.Api)
            .That().Inherit(typeof(ControllerBase))
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "BillingRecon.Infrastructure")
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void The_api_maps_no_minimal_api_endpoint()
    {
        // Controllers only (CLAUDE.md section 2). MapControllers, MapHealthChecks, MapOpenApi and MapScalarApiReference
        // register no endpoint of our own and are not matched.
        var minimalApi = new Regex(@"\.Map(Get|Post|Put|Delete|Patch|Methods|Group|Fallback)\s*\(");

        foreach (var file in RepositoryFiles.Find("src/BillingRecon.Api", "*.cs"))
        {
            minimalApi.IsMatch(File.ReadAllText(RepositoryFiles.PathOf(file))).ShouldBeFalse($"{file} maps a minimal API endpoint");
        }
    }

    [Fact]
    public void The_audit_trail_is_the_only_subscriber_of_domain_events()
    {
        // An effect in another module is a call through that module's port in the use case, where it can be seen,
        // never a subscription (docs/domain-model.md section 8; ADR 0003, ADR 0020). A second subscriber needs an ADR.
        var subscribers = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && typeof(IDomainEventSubscriber).IsAssignableFrom(type))
            .Select(type => type.FullName);

        subscribers.ShouldBe(["BillingRecon.Application.Audit.AuditSubscriber"]);
    }
}
