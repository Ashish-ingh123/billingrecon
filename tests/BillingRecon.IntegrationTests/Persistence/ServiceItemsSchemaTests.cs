using BillingRecon.Domain.Billing;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The ServiceItems table as the production model describes it, compared with docs/database.md section 3.3. The DDL is
/// generated from the model, which needs no database; what the migrations contain is checked in
/// <see cref="MigrationsTests"/>.
/// </summary>
public sealed class ServiceItemsSchemaTests : IDisposable
{
    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();
    private readonly string _ddl;

    public ServiceItemsSchemaTests() => _ddl = _context.Database.GenerateCreateScript();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[Code] varchar(30) NOT NULL")]
    [InlineData("[Name] nvarchar(200) NOT NULL")]
    [InlineData("[Category] nvarchar(50) NULL")]
    [InlineData("[DefaultUnitPrice] decimal(19,4) NOT NULL")]
    [InlineData("[TaxRatePercent] decimal(5,2) NOT NULL")]
    [InlineData("[Status] varchar(20) NOT NULL")]
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[CreatedByUserId] uniqueidentifier NULL")]
    [InlineData("[UpdatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[UpdatedByUserId] uniqueidentifier NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    [InlineData("CONSTRAINT [PK_ServiceItems] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_ServiceItems_DefaultUnitPrice] CHECK ([DefaultUnitPrice] >= 0)")]
    [InlineData("CONSTRAINT [CK_ServiceItems_TaxRatePercent] CHECK ([TaxRatePercent] BETWEEN 0 AND 100)")]
    [InlineData("CONSTRAINT [CK_ServiceItems_Status] CHECK ([Status] IN ('Active', 'Inactive'))")]
    [InlineData("CONSTRAINT [FK_ServiceItems_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])")]
    public void The_table_has_the_documented_column_or_constraint(string fragment)
    {
        Table("ServiceItems").ShouldContain(fragment, Case.Sensitive);
    }

    [Fact]
    public void The_table_has_exactly_the_documented_columns()
    {
        var columns = _context.Model.FindEntityType(typeof(ServiceItem))!.GetProperties().Select(property => property.GetColumnName());

        columns.ShouldBe(
            [
                "Id", "TenantId", "Code", "Name", "Category", "DefaultUnitPrice", "TaxRatePercent", "Status",
                "CreatedAtUtc", "CreatedByUserId", "UpdatedAtUtc", "UpdatedByUserId", "RowVersion",
            ],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("CREATE UNIQUE INDEX [UX_ServiceItems_TenantId_Code] ON [ServiceItems] ([TenantId], [Code]);")]
    [InlineData("CREATE INDEX [IX_ServiceItems_TenantId_Name] ON [ServiceItems] ([TenantId], [Name]);")]
    public void The_documented_indexes_exist(string index)
    {
        _ddl.ShouldContain(index, Case.Sensitive);
    }

    [Fact]
    public void The_table_has_no_index_beyond_the_documented_ones()
    {
        var indexes = _ddl.Split('\n')
            .Where(line => line.Contains(" INDEX [", StringComparison.Ordinal) && line.Contains("ON [ServiceItems]", StringComparison.Ordinal))
            .ToArray();

        indexes.Length.ShouldBe(2, string.Join(Environment.NewLine, indexes));
    }

    private string Table(string name)
    {
        var start = _ddl.IndexOf($"CREATE TABLE [{name}]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"the model has no table {name}");
        var end = _ddl.IndexOf(");", start, StringComparison.Ordinal);

        return _ddl[start..end];
    }
}
