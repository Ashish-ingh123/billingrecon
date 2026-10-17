using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// The ban list (<c>src/BannedSymbols.txt</c>) is enforced by an analyzer at build time (RS0030). The analyzer
/// ignores an entry that names nothing, and a suppression switches it off, both silently. These tests close the
/// two gaps: every entry must name a real member of the referenced framework and EF Core versions, and the rule
/// may be suppressed only where a decision allows it (ADR 0015).
/// </summary>
public sealed class BannedSymbolsTests
{
    private const string BanList = "src/BannedSymbols.txt";

    /// <summary>The only files that may suppress RS0030, each for one named reason.</summary>
    private static readonly string[] SanctionedSuppressions =
    [
        // Reads TimeProvider.System, once, so that everything else can take IClock (ADR 0010).
        "src/BillingRecon.Infrastructure/Common/Time/SystemClock.cs",
    ];

    private static readonly Assembly[] SearchedAssemblies =
    [
        typeof(object).Assembly,
        typeof(DbContext).Assembly,
        typeof(RelationalDatabaseFacadeExtensions).Assembly,
    ];

    public static TheoryData<string> BannedIds()
    {
        var data = new TheoryData<string>();
        foreach (var (id, _) in ReadEntries())
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BannedIds))]
    public void Every_banned_symbol_names_exactly_one_existing_member(string id)
    {
        Resolve(id).ShouldBe(1, $"'{id}' in {BanList} must name exactly one member. If the API changed shape in an upgrade, update the entry.");
    }

    // The list is the decision (ADR 0010, ADR 0015); this pins it, so an entry cannot be dropped without a test changing.
    [Theory]
    [InlineData("P:System.DateTime.Now")]
    [InlineData("P:System.DateTime.UtcNow")]
    [InlineData("P:System.DateTime.Today")]
    [InlineData("P:System.DateTimeOffset.Now")]
    [InlineData("P:System.DateTimeOffset.UtcNow")]
    [InlineData("P:System.TimeProvider.System")]
    public void The_ways_to_read_the_system_time_are_banned(string id)
    {
        ReadEntries().Select(entry => entry.Id).ShouldContain(id);
    }

    [Theory]
    [InlineData("P:System.TimeZoneInfo.Local")]
    [InlineData("P:System.TimeProvider.LocalTimeZone")]
    [InlineData("M:System.TimeProvider.GetLocalNow")]
    [InlineData("M:System.DateTime.ToLocalTime")]
    [InlineData("M:System.DateTimeOffset.ToLocalTime")]
    [InlineData("P:System.DateTimeOffset.LocalDateTime")]
    public void The_ways_to_use_the_time_zone_of_the_machine_are_banned(string id)
    {
        ReadEntries().Select(entry => entry.Id).ShouldContain(id);
    }

    // EF Core reads these back by key with the query filters switched off, so they would cross tenants.
    [Theory]
    [InlineData("M:Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry.Reload")]
    [InlineData("M:Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry.ReloadAsync(System.Threading.CancellationToken)")]
    [InlineData("M:Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry.GetDatabaseValues")]
    [InlineData("M:Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry.GetDatabaseValuesAsync(System.Threading.CancellationToken)")]
    public void The_calls_that_reload_an_entity_without_the_tenant_filter_are_banned(string id)
    {
        ReadEntries().Select(entry => entry.Id).ShouldContain(id);
    }

    [Fact]
    public void Every_way_to_reload_an_entity_entry_is_among_them()
    {
        // If EF Core grows another Reload or GetDatabaseValues overload, it has to be banned as well.
        var reloads = typeof(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Reload", StringComparison.Ordinal) || method.Name.StartsWith("GetDatabaseValues", StringComparison.Ordinal))
            .Select(DocumentationId.Of)
            .ToArray();

        reloads.ShouldNotBeEmpty();
        ReadEntries().Select(entry => entry.Id).Where(id => reloads.Contains(id)).ShouldBe(reloads, ignoreOrder: true);
    }

    [Theory]
    [InlineData("IgnoreQueryFilters")]
    [InlineData("ExecuteUpdate")]
    [InlineData("ExecuteUpdateAsync")]
    [InlineData("ExecuteDelete")]
    [InlineData("ExecuteDeleteAsync")]
    public void Every_overload_of_the_banned_ef_core_methods_is_listed(string method)
    {
        var overloads = typeof(EntityFrameworkQueryableExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => candidate.Name == method)
            .Select(DocumentationId.Of)
            .ToArray();

        overloads.ShouldNotBeEmpty();
        ReadEntries().Select(entry => entry.Id).Where(id => overloads.Contains(id)).ShouldBe(overloads, ignoreOrder: true);
    }

    [Fact]
    public void Every_entry_says_what_to_do_instead()
    {
        ReadEntries().ShouldAllBe(entry => entry.Message.Length > 20);
    }

    [Fact]
    public void The_rule_is_suppressed_only_in_the_sanctioned_files()
    {
        var suppressing = RepositoryFiles.Find("src", "*.cs")
            .Where(path => File.ReadAllText(RepositoryFiles.PathOf(path)).Contains("RS0030", StringComparison.Ordinal))
            .ToArray();

        suppressing.ShouldBe(SanctionedSuppressions, ignoreOrder: true);
    }

    [Fact]
    public void No_project_or_build_file_switches_the_rule_off()
    {
        var buildFiles = RepositoryFiles.Find("src", "*.csproj")
            .Concat(RepositoryFiles.Find("src", "*.props"))
            .Concat(RepositoryFiles.Find("src", "*.targets"))
            .Concat(RepositoryFiles.Find("src", ".editorconfig"))
            .Concat(RepositoryFiles.Find("src", ".globalconfig"))
            .Append("Directory.Build.props");

        foreach (var file in buildFiles)
        {
            File.ReadAllText(RepositoryFiles.PathOf(file)).ShouldNotContain("RS0030", customMessage: $"{file} must not configure RS0030");
        }

        var rule = File.ReadAllLines(RepositoryFiles.PathOf(".editorconfig")).Where(line => line.Contains("RS0030", StringComparison.Ordinal) && !line.TrimStart().StartsWith('#'));
        rule.ShouldBe(["dotnet_diagnostic.RS0030.severity = error"]);
    }

    [Fact]
    public void Every_project_under_src_gets_the_analyzer_and_the_ban_list()
    {
        // src/Directory.Build.props applies to every project below it, unless a project stops importing it.
        var props = File.ReadAllText(RepositoryFiles.PathOf("src/Directory.Build.props"));
        props.ShouldContain("<PackageReference Include=\"Microsoft.CodeAnalysis.BannedApiAnalyzers\"");
        props.ShouldContain("<AdditionalFiles Include=\"$(MSBuildThisFileDirectory)BannedSymbols.txt\"");

        RepositoryFiles.Find("src", "Directory.Build.props").ShouldBe(["src/Directory.Build.props"]);
    }

    // The file format of the analyzer: "//" starts a comment, the id ends at the first ";".
    private static (string Id, string Message)[] ReadEntries()
        => [.. File.ReadAllLines(RepositoryFiles.PathOf(BanList))
            .Select(line => line.IndexOf("//", StringComparison.Ordinal) is var comment and >= 0 ? line[..comment] : line)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.Split(';', 2))
            .Select(parts => (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty))];

    private static int Resolve(string id)
    {
        var kind = id[..2];
        var signature = id[2..];
        var parameters = signature.IndexOf('(', StringComparison.Ordinal);
        var qualifiedName = parameters < 0 ? signature : signature[..parameters];
        var typeName = qualifiedName[..qualifiedName.LastIndexOf('.')];

        var type = SearchedAssemblies.Select(assembly => assembly.GetType(typeName)).FirstOrDefault(found => found is not null);
        if (type is null)
        {
            return 0;
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var ids = kind switch
        {
            "P:" => type.GetProperties(all).Select(DocumentationId.Of),
            "M:" => type.GetMethods(all).Select(DocumentationId.Of),
            _ => [],
        };

        return ids.Count(candidate => candidate == id);
    }
}
