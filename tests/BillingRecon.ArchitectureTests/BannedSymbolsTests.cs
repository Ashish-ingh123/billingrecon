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

        // Raw SQL, one statement each (ADR 0021): the locked read-and-increment of a number series (ADR 0008),
        "src/BillingRecon.Infrastructure/Common/Numbering/NumberSeriesAllocator.cs",

        // the locked read and change of the cached balances of a payer, which Entity Framework cannot express (ADR 0005),
        "src/BillingRecon.Infrastructure/Receivables/ReceivablesLedger.cs",

        // and the read-only reconciliation of the ledger with its caches (ADR 0005; docs/database.md section 5.6).
        "src/BillingRecon.Infrastructure/Receivables/ReconciliationQuery.cs",

        // The query filters switched off once, for the user a sign-in looks for: it has no tenant yet (ADR 0015, ADR 0031).
        "src/BillingRecon.Infrastructure/Identity/LoginUserLookup.cs",
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

    // Raw SQL goes around the tenant filter, the save guards and the audit stamps (ADR 0015); the number series is
    // the one sanctioned use (ADR 0008). Every entry point EF Core has, and any it grows, must be on the list.
    [Theory]
    [InlineData(typeof(RelationalDatabaseFacadeExtensions), "ExecuteSql")]
    [InlineData(typeof(RelationalDatabaseFacadeExtensions), "SqlQuery")]
    [InlineData(typeof(RelationalDatabaseFacadeExtensions), "GetDbConnection")]
    [InlineData(typeof(RelationalQueryableExtensions), "FromSql")]
    public void Every_raw_sql_entry_point_of_ef_core_is_banned(Type owner, string namePrefix)
    {
        var entryPoints = owner.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => candidate.Name.StartsWith(namePrefix, StringComparison.Ordinal))
            .Select(DocumentationId.Of)
            .ToArray();

        entryPoints.ShouldNotBeEmpty();
        ReadEntries().Select(entry => entry.Id).Where(id => entryPoints.Contains(id)).ShouldBe(entryPoints, ignoreOrder: true);
    }

    [Fact]
    public void The_number_series_allocator_sends_two_statements_made_of_constants_and_parameters_within_the_tenant()
    {
        var allocator = File.ReadAllText(RepositoryFiles.PathOf("src/BillingRecon.Infrastructure/Common/Numbering/NumberSeriesAllocator.cs"));

        // Two suppressions, each around one call, and the SQL texts are constants: nothing is ever spliced into them.
        // TakeNext takes a number (ADR 0008); HoldSeries is its locked read alone, for a prefix change (ADR 0033, built
        // note point 5). Both lock the series of the current tenant, by the same key.
        allocator.Split("#pragma warning disable RS0030").Length.ShouldBe(3);
        allocator.Split("SqlQueryRaw").Length.ShouldBe(3);
        allocator.ShouldContain("private const string TakeNext =");
        allocator.ShouldContain("private const string HoldSeries =");
        allocator.Split("WITH (UPDLOCK, HOLDLOCK)").Length.ShouldBe(3);
        allocator.Split("WHERE  [TenantId] = @tenant AND [DocumentType] = @type AND [FiscalYear] = @fiscalYear").Length.ShouldBe(4);
        allocator.ShouldNotContain("$\"\"\"");
        allocator.ShouldNotContain("string.Format");
        allocator.ShouldNotContain("ExecuteSql");
        allocator.ShouldNotContain("FromSql");
    }

    [Theory]
    [InlineData("src/BillingRecon.Infrastructure/Receivables/ReceivablesLedger.cs", "private const string MoveBalances =")]
    [InlineData("src/BillingRecon.Infrastructure/Receivables/ReconciliationQuery.cs", "private const string FindDrift =")]
    public void Each_receivables_file_that_sends_raw_sql_sends_one_statement_made_of_constants_and_parameters_within_the_tenant(string path, string constant)
    {
        var source = File.ReadAllText(RepositoryFiles.PathOf(path));

        // One suppression, around one call, the SQL text is a constant, and it never leaves the current tenant (ADR 0021).
        source.Split("#pragma warning disable RS0030").Length.ShouldBe(2);
        source.Split("SqlQueryRaw<").Length.ShouldBe(2);
        source.ShouldContain(constant);
        source.ShouldContain("[TenantId] = @tenant");
        source.ShouldNotContain("$\"\"\"");
        source.ShouldNotContain("string.Format");
        source.ShouldNotContain("ExecuteSql");
        source.ShouldNotContain("FromSql");
    }

    [Fact]
    public void BR404_the_ledger_reads_the_balances_of_the_payer_under_an_update_lock_before_it_changes_them()
    {
        var ledger = File.ReadAllText(RepositoryFiles.PathOf("src/BillingRecon.Infrastructure/Receivables/ReceivablesLedger.cs"));

        var read = ledger.IndexOf("WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal);
        var change = ledger.IndexOf("UPDATE [dbo].[PayerBalances]", StringComparison.Ordinal);

        read.ShouldBeGreaterThan(0, "the locked read is gone");
        change.ShouldBeGreaterThan(read, "the change must follow the locked read");
    }

    [Fact]
    public void The_sign_in_lookup_switches_the_query_filters_off_once_and_names_the_tenant_in_that_query()
    {
        var source = File.ReadAllText(RepositoryFiles.PathOf("src/BillingRecon.Infrastructure/Identity/LoginUserLookup.cs"));

        // One suppression, around one query, whose predicate is the tenant the sign-in names (ADR 0031, point 6).
        source.Split("#pragma warning disable RS0030").Length.ShouldBe(2);
        source.Split(".IgnoreQueryFilters()").Length.ShouldBe(2);
        var start = source.IndexOf("#pragma warning disable RS0030", StringComparison.Ordinal);
        var end = source.IndexOf("#pragma warning restore RS0030", StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var suppressed = source[start..end];
        suppressed.ShouldContain(".IgnoreQueryFilters()");
        suppressed.ShouldContain("candidate.TenantId == tenantId");
        suppressed.ShouldContain(".AsNoTracking()");

        foreach (var other in new[] { "SqlQuery", "ExecuteSql", "FromSql", "ExecuteUpdate", "ExecuteDelete", "Reload", "GetDatabaseValues" })
        {
            source.ShouldNotContain(other);
        }
    }

    [Fact]
    public void The_reconciliation_only_reads()
    {
        var source = File.ReadAllText(RepositoryFiles.PathOf("src/BillingRecon.Infrastructure/Receivables/ReconciliationQuery.cs"));

        foreach (var verb in new[] { "UPDATE ", "INSERT ", "DELETE ", "MERGE ", "TRUNCATE ", "EXEC " })
        {
            source.ShouldNotContain(verb, Case.Sensitive);
        }
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
