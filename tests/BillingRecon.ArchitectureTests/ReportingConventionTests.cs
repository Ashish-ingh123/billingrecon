using System.Reflection;
using System.Text.RegularExpressions;
using BillingRecon.Api.Reporting;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Reporting only reads (BR-901; docs/domain-model.md section 2; ADR 0036). It may read the tables of every business
/// module (the exceptions of <see cref="ModuleDependencyTests"/>), so these tests keep it from writing any: no port that
/// writes, no call that changes or saves an entity, every set it reads untracked, and only GET actions.
/// </summary>
public sealed partial class ReportingConventionTests
{
    // The ports through which a use case changes state; Reporting takes none of them.
    private static readonly string[] WritingPorts =
    [
        "BillingRecon.Application.Common.IUnitOfWork",
        "BillingRecon.Application.Common.IIdempotencyStore",
        "BillingRecon.Application.Common.INumberSeries",
        "BillingRecon.Application.Receivables.IReceivablesLedger",
        "BillingRecon.Application.Receivables.IPayerAccounts",
    ];

    [Fact]
    public void Reporting_takes_no_port_that_writes_and_no_repository()
    {
        var reporting = Types.InAssemblies(Assemblies.All).That().ResideInNamespaceMatching(@"^BillingRecon\.\w+\.Reporting(\..*)?$");

        reporting.ShouldNot().HaveDependencyOnAny(WritingPorts).GetResult().ShouldPass();

        var repositories = Assemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsInterface && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToArray();
        repositories.ShouldNotBeEmpty();
        Types.InAssemblies(Assemblies.All).That().ResideInNamespaceMatching(@"^BillingRecon\.\w+\.Reporting(\..*)?$")
            .ShouldNot().HaveDependencyOnAny(repositories).GetResult().ShouldPass();
    }

    [Fact]
    public void Reporting_never_changes_or_saves_an_entity_and_reads_every_set_untracked()
    {
        var files = RepositoryFiles.Find("src", "*.cs")
            .Where(path => path.Contains("/Reporting/", StringComparison.Ordinal))
            .ToList();
        files.ShouldNotBeEmpty();

        foreach (var path in files)
        {
            var text = File.ReadAllText(RepositoryFiles.PathOf(path));

            WritingCall().Matches(text).Select(match => $"{path}: {match.Value}").ShouldBeEmpty();
            SetWithoutAsNoTracking().Matches(text).Select(match => $"{path}: {match.Value}").ShouldBeEmpty();
        }
    }

    [Fact]
    public void Every_action_of_the_reports_and_the_dashboard_is_a_GET()
    {
        var controllers = typeof(ReportsController).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ReportsController).Namespace && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type))
            .ToList();
        controllers.Select(type => type.Name).ShouldBe(["DashboardController", "ReportsController"], ignoreOrder: true);

        var methods = controllers
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.HttpMethods)
            .Distinct();

        methods.ShouldBe(["GET"]);
    }

    // A call that changes what the context tracks, saves it, or sends SQL of its own.
    [GeneratedRegex(@"\.(SaveChanges|SaveChangesAsync|Add|AddRange|AddAsync|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange|AsTracking|ExecuteSql\w*|ExecuteUpdate\w*|ExecuteDelete\w*)\(")]
    private static partial Regex WritingCall();

    // A set of the context read without AsNoTracking() right after it.
    [GeneratedRegex(@"Set<\w+>\(\)(?!\.AsNoTracking\(\))")]
    private static partial Regex SetWithoutAsNoTracking();
}
