using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// What the conventions of the context make of the probe entities, read from the DDL Entity Framework generates for
/// the test model (no database needed). The probe configurations set lengths and precision only; everything else
/// asserted here comes from the conventions (docs/database.md section 1, ADR 0015).
/// </summary>
public sealed class ConventionSchemaTests : IDisposable
{
    private readonly TestDbContext _context = TestContexts.Create(FixedIdentity.Random());
    private readonly string _ddl;

    public ConventionSchemaTests() => _ddl = _context.Database.GenerateCreateScript();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[Name] nvarchar(100) NOT NULL")]
    [InlineData("[Price] decimal(18,2) NOT NULL")]
    [InlineData("[Kind] varchar(20) NOT NULL")] // an enum, stored by name
    [InlineData("[PreviousKind] varchar(20) NULL")] // an optional enum
    [InlineData("[ShippedDate] date NULL")] // an optional business date
    [InlineData("[ArchivedAtUtc] datetimeoffset(3) NULL")] // an optional instant
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[CreatedByUserId] uniqueidentifier NULL")]
    [InlineData("[UpdatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[UpdatedByUserId] uniqueidentifier NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    [InlineData("CONSTRAINT [PK_Widget] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_Widget_Kind] CHECK ([Kind] IN ('Plain', 'Fancy'))")]
    [InlineData("CONSTRAINT [CK_Widget_PreviousKind] CHECK ([PreviousKind] IN ('Plain', 'Fancy'))")]
    public void A_mutable_tenant_scoped_aggregate_gets_its_columns_and_constraints_by_convention(string fragment)
    {
        Table("Widget").ShouldContain(fragment, Case.Sensitive);
    }

    [Fact]
    public void An_append_only_entity_has_creation_columns_but_no_modification_columns_and_no_row_version()
    {
        var table = Table("LogLine");

        table.ShouldContain("[CreatedAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        table.ShouldContain("[CreatedByUserId] uniqueidentifier NULL", Case.Sensitive);
        table.ShouldContain("[TenantId] uniqueidentifier NOT NULL", Case.Sensitive);
        table.ShouldNotContain("UpdatedAtUtc");
        table.ShouldNotContain("UpdatedByUserId");
        table.ShouldNotContain("RowVersion");
    }

    [Fact]
    public void A_deliberately_unbounded_column_is_a_max_column_and_a_code_is_a_varchar()
    {
        var table = Table("Setting");

        table.ShouldContain("[Json] nvarchar(max) NULL", Case.Sensitive);
        table.ShouldContain("[Key] varchar(50) NOT NULL", Case.Sensitive);
        table.ShouldNotContain("TenantId");
    }

    [Fact]
    public void The_domain_events_of_an_aggregate_are_not_a_column()
    {
        _ddl.ShouldNotContain("DomainEvents");
    }

    private string Table(string name)
    {
        var start = _ddl.IndexOf($"CREATE TABLE [{name}]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"the model has no table {name}");
        var end = _ddl.IndexOf(");", start, StringComparison.Ordinal);

        return _ddl[start..end];
    }
}
