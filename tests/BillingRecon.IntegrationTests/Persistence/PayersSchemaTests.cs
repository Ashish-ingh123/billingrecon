using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The Patients and Payers tables as the production model describes them, compared with docs/database.md section 3.2.
/// The DDL is generated from the model, which needs no database; what the migrations contain is checked in
/// <see cref="MigrationsTests"/>.
/// </summary>
public sealed class PayersSchemaTests : IDisposable
{
    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();
    private readonly string _ddl;

    public PayersSchemaTests() => _ddl = _context.Database.GenerateCreateScript();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[Mrn] nvarchar(30) NOT NULL")]
    [InlineData("[FullName] nvarchar(200) NOT NULL")]
    [InlineData("[DateOfBirth] date NULL")]
    [InlineData("[Sex] varchar(10) NULL")]
    [InlineData("[Phone] nvarchar(20) NULL")]
    [InlineData("[Email] nvarchar(256) NULL")]
    [InlineData("[AddressLine1] nvarchar(200) NULL")]
    [InlineData("[AddressLine2] nvarchar(200) NULL")]
    [InlineData("[City] nvarchar(100) NULL")]
    [InlineData("[State] nvarchar(100) NULL")]
    [InlineData("[PinCode] varchar(10) NULL")]
    [InlineData("[Status] varchar(20) NOT NULL")]
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[CreatedByUserId] uniqueidentifier NULL")]
    [InlineData("[UpdatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[UpdatedByUserId] uniqueidentifier NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    [InlineData("CONSTRAINT [PK_Patients] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_Patients_Sex] CHECK ([Sex] IN ('Male', 'Female', 'Other', 'Unknown'))")]
    [InlineData("CONSTRAINT [CK_Patients_Status] CHECK ([Status] IN ('Active', 'Inactive'))")]
    [InlineData("CONSTRAINT [FK_Patients_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])")]
    public void The_patients_table_has_the_documented_column_or_constraint(string fragment)
    {
        Table("Patients").ShouldContain(fragment, Case.Sensitive);
    }

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[TenantId] uniqueidentifier NOT NULL")]
    [InlineData("[Kind] varchar(20) NOT NULL")]
    [InlineData("[Name] nvarchar(200) NOT NULL")]
    [InlineData("[PatientId] uniqueidentifier NULL")]
    [InlineData("[Phone] nvarchar(20) NULL")]
    [InlineData("[Email] nvarchar(256) NULL")]
    [InlineData("[Status] varchar(20) NOT NULL")]
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[CreatedByUserId] uniqueidentifier NULL")]
    [InlineData("[UpdatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[UpdatedByUserId] uniqueidentifier NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    [InlineData("CONSTRAINT [PK_Payers] PRIMARY KEY ([Id])")]
    [InlineData("CONSTRAINT [CK_Payers_Kind] CHECK ([Kind] IN ('SelfPay', 'Insurer', 'Corporate'))")]
    [InlineData("CONSTRAINT [CK_Payers_Status] CHECK ([Status] IN ('Active', 'Inactive'))")]
    [InlineData("CONSTRAINT [FK_Payers_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])")]
    [InlineData("CONSTRAINT [FK_Payers_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])")]
    public void The_payers_table_has_the_documented_column_or_constraint(string fragment)
    {
        Table("Payers").ShouldContain(fragment, Case.Sensitive);
    }

    [Fact]
    public void Each_table_has_exactly_the_documented_columns()
    {
        Columns<Patient>().ShouldBe(
            [
                "Id", "TenantId", "Mrn", "FullName", "DateOfBirth", "Sex", "Phone", "Email", "AddressLine1", "AddressLine2",
                "City", "State", "PinCode", "Status", "CreatedAtUtc", "CreatedByUserId", "UpdatedAtUtc", "UpdatedByUserId", "RowVersion",
            ],
            ignoreOrder: true);

        Columns<Payer>().ShouldBe(
            [
                "Id", "TenantId", "Kind", "Name", "PatientId", "Phone", "Email", "Status",
                "CreatedAtUtc", "CreatedByUserId", "UpdatedAtUtc", "UpdatedByUserId", "RowVersion",

                // Since Prompt 10.3: the reference a guarantor had in the system before (migration 0023_OpeningBalances).
                "LegacyReference",
            ],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("CREATE UNIQUE INDEX [UX_Patients_TenantId_Mrn] ON [Patients] ([TenantId], [Mrn]);")]
    [InlineData("CREATE INDEX [IX_Patients_TenantId_FullName] ON [Patients] ([TenantId], [FullName]);")]
    [InlineData("CREATE INDEX [IX_Payers_TenantId_Name] ON [Payers] ([TenantId], [Name]);")]
    [InlineData("CREATE UNIQUE INDEX [UX_Payers_TenantId_PatientId] ON [Payers] ([TenantId], [PatientId]) WHERE [PatientId] IS NOT NULL;")]
    [InlineData("CREATE UNIQUE INDEX [UX_Payers_TenantId_LegacyReference] ON [Payers] ([TenantId], [LegacyReference]) WHERE [LegacyReference] IS NOT NULL;")]
    public void The_documented_indexes_exist(string index)
    {
        _ddl.ShouldContain(index, Case.Sensitive);
    }

    [Fact]
    public void The_tables_have_no_index_beyond_the_documented_ones()
    {
        // In particular none that Entity Framework would add for a foreign key, led by the key column (ADR 0015).
        var indexes = _ddl.Split('\n')
            .Where(line => line.Contains(" INDEX [", StringComparison.Ordinal) && (line.Contains("ON [Patients]", StringComparison.Ordinal) || line.Contains("ON [Payers]", StringComparison.Ordinal)))
            .ToArray();

        indexes.Length.ShouldBe(5, string.Join(Environment.NewLine, indexes));
    }

    [Fact]
    public void No_foreign_key_cascades()
    {
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
