using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;

namespace BillingRecon.ArchitectureTests;

/// <summary>The layer rule from CLAUDE.md section 3: Domain has no dependencies, Application depends on Domain only, Infrastructure never on Api.</summary>
public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_references_no_project_and_no_package()
    {
        ReferencedAssemblyNames(Assemblies.Domain)
            .ShouldAllBe(name => name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard");
    }

    [Fact]
    public void The_domain_project_file_declares_no_reference_of_any_kind()
    {
        // The assembly test above sees only the references the compiler kept: a package added to the project file and
        // not used yet would pass it. This one fails on the project file itself, so the rule holds from the first edit.
        var projectFile = File.ReadAllText(RepositoryFiles.PathOf("src/BillingRecon.Domain/BillingRecon.Domain.csproj"));

        projectFile.ShouldNotContain("<PackageReference", customMessage: "Domain must not reference a package");
        projectFile.ShouldNotContain("<ProjectReference", customMessage: "Domain must not reference a project");
        projectFile.ShouldNotContain("<FrameworkReference", customMessage: "Domain must not reference a shared framework");
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure_or_Api()
    {
        var references = ReferencedAssemblyNames(Assemblies.Application);

        references.ShouldNotContain("BillingRecon.Infrastructure");
        references.ShouldNotContain("BillingRecon.Api");
    }

    [Fact]
    public void Infrastructure_does_not_reference_Api()
    {
        ReferencedAssemblyNames(Assemblies.Infrastructure).ShouldNotContain("BillingRecon.Api");
    }

    [Fact]
    public void Domain_types_do_not_depend_on_outer_layers_or_frameworks()
    {
        Types.InAssembly(Assemblies.Domain)
            .ShouldNot().HaveDependencyOnAny(
                "BillingRecon.Application",
                "BillingRecon.Infrastructure",
                "BillingRecon.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void Application_types_do_not_depend_on_Infrastructure_or_Api()
    {
        Types.InAssembly(Assemblies.Application)
            .ShouldNot().HaveDependencyOnAny("BillingRecon.Infrastructure", "BillingRecon.Api")
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void Infrastructure_types_do_not_depend_on_Api()
    {
        Types.InAssembly(Assemblies.Infrastructure)
            .ShouldNot().HaveDependencyOn("BillingRecon.Api")
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void Domain_uses_no_floating_point_type_anywhere()
    {
        // CLAUDE.md section 4, rule 1: money is decimal, never float or double. Nothing in the Domain may take, return
        // or hold a floating-point value, whatever its visibility, so a double cannot reach Money.Round by accident.
        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Type[] floatingPoint = [typeof(double), typeof(float), typeof(Half)];

        var offenders = new List<string>();
        foreach (var type in Assemblies.Domain.GetTypes().Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
        {
            var members = type.GetFields(everything).Select(field => (Member: field.Name, Types: new[] { field.FieldType }))
                .Concat(type.GetProperties(everything).Select(property => (Member: property.Name, Types: new[] { property.PropertyType })))
                .Concat(type.GetMethods(everything).Select(method => (Member: method.Name, Types: method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType).ToArray())))
                .Concat(type.GetConstructors(everything).Select(constructor => (Member: ".ctor", Types: constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray())));

            offenders.AddRange(members
                .Where(member => member.Types.Select(memberType => Nullable.GetUnderlyingType(memberType) ?? memberType).Any(floatingPoint.Contains))
                .Select(member => $"{type.FullName}.{member.Member}"));
        }

        offenders.ShouldBeEmpty("the Domain must not use double, float or Half");
    }

    private static string[] ReferencedAssemblyNames(Assembly assembly)
        => assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
}
