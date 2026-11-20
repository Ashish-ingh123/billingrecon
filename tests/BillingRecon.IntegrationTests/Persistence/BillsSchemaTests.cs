using BillingRecon.Domain.Billing;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The Bills and BillLines tables as the production model describes them, compared with docs/database.md section 3.3.
/// The DDL is generated from the model, which needs no database; what the migrations contain is checked in
/// <see cref="MigrationsTests"/>.
/// </summary>
public sealed class BillsSchemaTests : IDisposable
{
    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();
    private readonly string _ddl;

    public BillsSchemaTests() => _ddl = _context.Database.GenerateCreateScript();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[PatientId] uniqueidentifier NOT NULL")]
    [InlineData("[PayerId] uniqueidentifier NOT NULL")]
    [InlineData("[BillDate] date NOT NULL")]
    [InlineData("[DueDate] date NOT NULL")]
    [InlineData("[EncounterReference] nvarchar(50) NULL")]
    [InlineData("[Notes] nvarchar(1000) NULL")]
    [InlineData("[Status] varchar(20) NOT NULL")]
    [InlineData("[SubTotal] decimal(18,2) NOT NULL")]
    [InlineData("[DiscountTotal] decimal(18,2) NOT NULL")]
    [InlineData("[TaxTotal] decimal(18,2) NOT NULL")]
    [InlineData("[Total] decimal(18,2) NOT NULL")]
    [InlineData("[BalanceDue] decimal(18,2) NOT NULL")]
    [InlineData("[VoidedAtUtc] datetimeoffset(3) NULL")]
    [InlineData("[VoidedByUserId] uniqueidentifier NULL")]
    [InlineData("[VoidReason] nvarchar(500) NULL")]
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[CreatedByUserId] uniqueidentifier NULL")]
    [InlineData("[UpdatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[UpdatedByUserId] uniqueidentifier NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    [InlineData("CONSTRAINT [PK_Bills] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_Bills_Status] CHECK ([Status] IN ('Draft', 'Voided', 'Posted', 'PartiallyPaid', 'Settled'))")]
    [InlineData("CONSTRAINT [CK_Bills_DueDate] CHECK ([DueDate] >= [BillDate])")]
    [InlineData("CONSTRAINT [CK_Bills_SubTotal] CHECK ([SubTotal] >= 0)")]
    [InlineData("CONSTRAINT [CK_Bills_DiscountTotal] CHECK ([DiscountTotal] >= 0)")]
    [InlineData("CONSTRAINT [CK_Bills_TaxTotal] CHECK ([TaxTotal] >= 0)")]
    [InlineData("CONSTRAINT [CK_Bills_Total] CHECK ([Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal])")]
    [InlineData("CONSTRAINT [CK_Bills_BalanceDue] CHECK ([BalanceDue] BETWEEN 0 AND [Total])")]
    [InlineData("CONSTRAINT [CK_Bills_VoidedIffVoided] CHECK (([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL))")]
    [InlineData("CONSTRAINT [FK_Bills_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])")]
    [InlineData("CONSTRAINT [FK_Bills_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])")]
    [InlineData("CONSTRAINT [FK_Bills_PayerId] FOREIGN KEY ([PayerId]) REFERENCES [Payers] ([Id])")]
    public void The_bills_table_has_the_documented_column_or_constraint(string fragment)
    {
        Table("Bills").ShouldContain(fragment, Case.Sensitive);
    }

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[BillId] uniqueidentifier NOT NULL")]
    [InlineData("[LineNo] int NOT NULL")]
    [InlineData("[ServiceItemId] uniqueidentifier NULL")]
    [InlineData("[Description] nvarchar(300) NOT NULL")]
    [InlineData("[Quantity] decimal(9,3) NOT NULL")]
    [InlineData("[UnitPrice] decimal(19,4) NOT NULL")]
    [InlineData("[LineAmount] decimal(18,2) NOT NULL")]
    [InlineData("[DiscountAmount] decimal(18,2) NOT NULL")]
    [InlineData("[TaxRatePercent] decimal(5,2) NOT NULL")]
    [InlineData("[TaxAmount] decimal(18,2) NOT NULL")]
    [InlineData("[LineTotal] decimal(18,2) NOT NULL")]
    [InlineData("CONSTRAINT [PK_BillLines] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_BillLines_LineNo] CHECK ([LineNo] >= 1)")]
    [InlineData("CONSTRAINT [CK_BillLines_Quantity] CHECK ([Quantity] > 0)")]
    [InlineData("CONSTRAINT [CK_BillLines_UnitPrice] CHECK ([UnitPrice] >= 0)")]
    [InlineData("CONSTRAINT [CK_BillLines_LineAmount] CHECK ([LineAmount] >= 0)")]
    [InlineData("CONSTRAINT [CK_BillLines_DiscountAmount] CHECK ([DiscountAmount] BETWEEN 0 AND [LineAmount])")]
    [InlineData("CONSTRAINT [CK_BillLines_TaxRatePercent] CHECK ([TaxRatePercent] BETWEEN 0 AND 100)")]
    [InlineData("CONSTRAINT [CK_BillLines_TaxAmount] CHECK ([TaxAmount] >= 0)")]
    [InlineData("CONSTRAINT [CK_BillLines_LineTotal] CHECK ([LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount])")]
    [InlineData("CONSTRAINT [FK_BillLines_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])")]
    [InlineData("CONSTRAINT [FK_BillLines_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id])")]
    [InlineData("CONSTRAINT [FK_BillLines_ServiceItemId] FOREIGN KEY ([ServiceItemId]) REFERENCES [ServiceItems] ([Id])")]
    public void The_bill_lines_table_has_the_documented_column_or_constraint(string fragment)
    {
        Table("BillLines").ShouldContain(fragment, Case.Sensitive);
    }

    [Fact]
    public void Each_table_has_exactly_the_documented_columns()
    {
        Columns<Bill>().ShouldBe(
            [
                "Id", "TenantId", "PatientId", "PayerId", "BillDate", "DueDate", "EncounterReference", "Notes", "Status",
                "SubTotal", "DiscountTotal", "TaxTotal", "Total", "BalanceDue", "VoidedAtUtc", "VoidedByUserId", "VoidReason",
                "CreatedAtUtc", "CreatedByUserId", "UpdatedAtUtc", "UpdatedByUserId", "RowVersion",

                // Since Prompt 3.5: what posting sets (migration 0009_BillPosting).
                "Number", "FiscalYear", "SequenceNumber", "PostedAtUtc", "PostedByUserId",
            ],
            ignoreOrder: true);

        // No row version and no audit columns: the bill's cover its lines (ADR 0007).
        Columns<BillLine>().ShouldBe(
            [
                "Id", "TenantId", "BillId", "LineNo", "ServiceItemId", "Description", "Quantity", "UnitPrice",
                "LineAmount", "DiscountAmount", "TaxRatePercent", "TaxAmount", "LineTotal",
            ],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("CREATE INDEX [IX_Bills_TenantId_PatientId] ON [Bills] ([TenantId], [PatientId]);")]
    [InlineData("CREATE INDEX [IX_Bills_TenantId_PayerId_Status] ON [Bills] ([TenantId], [PayerId], [Status]) INCLUDE ([BalanceDue], [Total]);")]
    [InlineData("CREATE INDEX [IX_Bills_TenantId_BillDate] ON [Bills] ([TenantId], [BillDate]);")]
    [InlineData("CREATE UNIQUE INDEX [UX_BillLines_TenantId_BillId_LineNo] ON [BillLines] ([TenantId], [BillId], [LineNo]);")]
    public void The_documented_indexes_exist(string index)
    {
        _ddl.ShouldContain(index, Case.Sensitive);
    }

    [Fact]
    public void The_tables_have_no_index_beyond_the_documented_ones()
    {
        // In particular none that Entity Framework would add for a foreign key, led by the key column (ADR 0015).
        var indexes = _ddl.Split('\n')
            .Where(line => line.Contains(" INDEX [", StringComparison.Ordinal) && (line.Contains("ON [Bills]", StringComparison.Ordinal) || line.Contains("ON [BillLines]", StringComparison.Ordinal)))
            .ToArray();

        // Four for drafts, and since Prompt 3.5 the two filtered unique indexes of the number (BR-801).
        indexes.Length.ShouldBe(6, string.Join(Environment.NewLine, indexes));
    }

    [Fact]
    public void The_lines_are_removed_by_entity_framework_and_by_nothing_in_the_database()
    {
        var toBill = _context.Model.FindEntityType(typeof(BillLine))!.GetForeignKeys().Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Bill));

        toBill.DeleteBehavior.ShouldBe(DeleteBehavior.ClientCascade);
        _ddl.ShouldNotContain("CASCADE", Case.Insensitive);
        _ddl.ShouldNotContain("SET NULL", Case.Insensitive);
    }

    private string[] Columns<TEntity>()
        => [.. _context.Model.FindEntityType(typeof(TEntity))!.GetProperties().Select(property => property.GetColumnName())];

    private string Table(string name)
    {
        var start = _ddl.IndexOf($"CREATE TABLE [{name}]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"the model has no table {name}");
        var end = _ddl.IndexOf(");", start, StringComparison.Ordinal);

        return _ddl[start..end];
    }
}
