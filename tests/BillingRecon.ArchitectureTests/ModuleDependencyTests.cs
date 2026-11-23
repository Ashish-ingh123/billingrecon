using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// The module rule from CLAUDE.md section 3 and ADR 0003 (amendment of 2026-10-17):
/// <list type="bullet">
/// <item>a business module depends on another business module only through that module's Application namespace
/// (ports, DTOs), never on its Domain, Infrastructure or Api namespace;</item>
/// <item>the Domain of a business module depends on no other business module at all;</item>
/// <item>a cross-cutting module (Common, Identity, Audit) depends on no business module;</item>
/// <item>every type of the production code lives in a known module of a known layer.</item>
/// </list>
/// The exceptions the design allows are listed in AllowedExceptions with their reason; a new one is added here and in
/// ADR 0003 together, never by relaxing the rule. The tests cannot see which type a dependency reaches for, so an
/// exception allows the whole target namespace and review keeps it to what its reason says. The rule passes trivially
/// until the modules have types.
/// </summary>
public sealed class ModuleDependencyTests
{
    private static readonly string[] ForbiddenLayers = ["Domain", "Infrastructure", "Api"];

    private static readonly AllowedDependency[] AllowedExceptions =
    [
        new(
            "BillingRecon.Application.Payments",
            "BillingRecon.Domain.Billing",
            "Allocating a payment, and reversing an allocation, command the Payment and the Bill in one transaction (docs/domain-model.md section 6; ADR 0003)."),
        new(
            "BillingRecon.Application.Adjustments",
            "BillingRecon.Domain.Billing",
            "Posting an adjustment commands the Adjustment and the Bill in one transaction (docs/domain-model.md section 6; ADR 0003)."),
        new(
            "BillingRecon.Infrastructure.Billing",
            "BillingRecon.Domain.Payers",
            "The Bills table has foreign keys to Patients and Payers (docs/database.md section 3.3); BillConfiguration names the two entity types to declare them and uses nothing else of them (ADR 0003, amendment of 2026-11-20)."),
        .. ReportingReads("Payers", "Billing", "Payments", "Receivables", "Adjustments"),
    ];

    // The query services of Reporting read the tables of every business module, joined and paged, and never write
    // (ReportingConventionTests); docs/domain-model.md section 2, ADR 0003 amendment of 2026-11-23, ADR 0036.
    private static AllowedDependency[] ReportingReads(params string[] modules)
        => [.. modules.Select(module => new AllowedDependency(
            "BillingRecon.Infrastructure.Reporting",
            $"BillingRecon.Domain.{module}",
            "The reports read every module's tables through query services that only read (BR-901; docs/domain-model.md section 2; ADR 0003, amendment of 2026-11-23; ADR 0036)."))];

    public static TheoryData<string> BusinessModules()
    {
        var data = new TheoryData<string>();
        foreach (var module in Assemblies.BusinessModules)
        {
            data.Add(module);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BusinessModules))]
    public void A_business_module_depends_on_other_business_modules_only_through_their_application_contracts(string module)
    {
        foreach (var other in Assemblies.BusinessModules.Where(other => other != module))
        {
            foreach (var layer in ForbiddenLayers)
            {
                var target = Namespace(layer, other);
                var sources = Types.InAssemblies(Assemblies.All).That().ResideInNamespaceMatching(ModulePattern(module));

                foreach (var exception in AllowedExceptions.Where(allowed => allowed.Target == target && Regex.IsMatch(allowed.Source, ModulePattern(module))))
                {
                    sources = sources.And().DoNotResideInNamespace(exception.Source);
                }

                sources.ShouldNot().HaveDependencyOn(target).GetResult().ShouldPass();
            }
        }
    }

    [Theory]
    [MemberData(nameof(BusinessModules))]
    public void The_domain_of_a_business_module_depends_on_no_other_business_module(string module)
    {
        var otherModules = Assemblies.BusinessModules
            .Where(other => other != module)
            .SelectMany(other => Assemblies.Layers.Select(layer => Namespace(layer, other)))
            .ToArray();

        Types.InAssembly(Assemblies.Domain)
            .That().ResideInNamespaceMatching(ModulePattern(module))
            .ShouldNot().HaveDependencyOnAny(otherModules)
            .GetResult()
            .ShouldPass();
    }

    [Theory]
    [InlineData("Common")]
    [InlineData("Identity")]
    [InlineData("Audit")]
    public void Cross_cutting_module_does_not_depend_on_business_modules(string module)
    {
        var forbidden = Assemblies.BusinessModules
            .SelectMany(business => Assemblies.Layers.Select(layer => Namespace(layer, business)))
            .ToArray();

        Types.InAssemblies(Assemblies.All)
            .That().ResideInNamespaceMatching(ModulePattern(module))
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult()
            .ShouldPass();
    }

    [Fact]
    public void Every_type_of_the_production_code_lives_in_a_known_module_of_a_known_layer()
    {
        var layers = string.Join('|', Assemblies.Layers);
        var modules = string.Join('|', Assemblies.BusinessModules.Concat(Assemblies.CrossCuttingModules));
        var known = new Regex($@"^{Regex.Escape(Assemblies.RootNamespace)}\.({layers})(\.({modules})(\..+)?)?$");

        var strays = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsNested && !IsGenerated(type))
            .Where(type => type.Namespace is not null && type.Namespace.StartsWith(Assemblies.RootNamespace + ".", StringComparison.Ordinal))
            .Where(type => !known.IsMatch(type.Namespace!))
            .Select(type => type.FullName)
            .ToArray();

        strays.ShouldBeEmpty("every type lives in BillingRecon.<Layer> or BillingRecon.<Layer>.<Module>, with any sub-namespace below the module");
    }

    [Fact]
    public void Every_allowed_exception_names_two_different_business_modules_and_cites_the_design()
    {
        foreach (var exception in AllowedExceptions)
        {
            Module(exception.Source).ShouldBeOneOf(Assemblies.BusinessModules);
            Module(exception.Target).ShouldBeOneOf(Assemblies.BusinessModules);
            Module(exception.Source).ShouldNotBe(Module(exception.Target));
            exception.Reason.ShouldContain("docs/");
        }
    }

    private static bool IsGenerated(Type type)
        => type.Name.StartsWith('<')
            || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || type.IsDefined(typeof(GeneratedCodeAttribute), inherit: false);

    private static string Module(string namespaceName) => namespaceName.Split('.')[2];

    private static string Namespace(string layer, string module) => $"{Assemblies.RootNamespace}.{layer}.{module}";

    private static string ModulePattern(string module) => $@"^{Assemblies.RootNamespace}\.\w+\.{module}(\.|$)";

    private sealed record AllowedDependency(string Source, string Target, string Reason);
}
