using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The Tenants table as the production model describes it, compared with docs/database.md section 3.1. The DDL is
/// generated from the model, which needs no database. What the migrations contain is checked in
/// <see cref="MigrationsTests"/>.
/// </summary>
public sealed class TenantsSchemaTests : IDisposable
{
    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();
    private readonly string _ddl;

    public TenantsSchemaTests() => _ddl = _context.Database.GenerateCreateScript();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("[Id] uniqueidentifier NOT NULL")]
    [InlineData("[Code] varchar(20) NOT NULL")]
    [InlineData("[Name] nvarchar(200) NOT NULL")]
    [InlineData("[Status] varchar(20) NOT NULL")]
    [InlineData("[PostingApprovalThreshold] decimal(18,2) NULL")]
    [InlineData("[MaxBackdateDays] int NOT NULL")]
    [InlineData("[DefaultDueDays] int NOT NULL")]
    [InlineData("[BillPrefix] varchar(3) NOT NULL")]
    [InlineData("[CreditNotePrefix] varchar(3) NOT NULL")]
    [InlineData("[PaymentPrefix] varchar(3) NOT NULL")]
    [InlineData("[AdjustmentPrefix] varchar(3) NOT NULL")]
    [InlineData("[RefundPrefix] varchar(3) NOT NULL")]
    [InlineData("[CreatedAtUtc] datetimeoffset(3) NOT NULL")]
    [InlineData("[RowVersion] rowversion NOT NULL")]
    public void The_table_has_the_documented_column(string column)
    {
        TenantsTable().ShouldContain(column, Case.Sensitive);
    }

    [Fact]
    public void The_table_has_exactly_the_documented_columns()
    {
        var columns = _context.Model.FindEntityType(typeof(Tenant))!.GetProperties().Select(property => property.GetColumnName());

        columns.ShouldBe(
            [
                "Id", "Code", "Name", "Status", "PostingApprovalThreshold", "MaxBackdateDays", "DefaultDueDays",
                "BillPrefix", "CreditNotePrefix", "PaymentPrefix", "AdjustmentPrefix", "RefundPrefix", "CreatedAtUtc", "RowVersion",

                // Since Prompt 10.4: the letterhead of the printed documents (migration 0024_PrintedDocuments).
                "LegalName", "AddressLine1", "AddressLine2", "City", "State", "PinCode", "Phone", "Email", "Gstin", "Signatory",

                // Since Prompt 10.4c: the signatory's designation and the hashes of the logo and the signature
                // (migration 0025_LetterheadImages).
                "SignatoryDesignation", "LogoSha256", "SignatureSha256",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_primary_key_is_the_id()
    {
        TenantsTable().ShouldContain("CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])", Case.Sensitive);
    }

    [Fact]
    public void The_code_is_unique()
    {
        _ddl.ShouldContain("CREATE UNIQUE INDEX [UX_Tenants_Code] ON [Tenants] ([Code])", Case.Sensitive);
    }

    [Theory]
    [InlineData("CONSTRAINT [CK_Tenants_Status] CHECK ([Status] IN ('Active', 'Suspended'))")]
    [InlineData("CONSTRAINT [CK_Tenants_MaxBackdateDays] CHECK ([MaxBackdateDays] BETWEEN 0 AND 365)")]
    [InlineData("CONSTRAINT [CK_Tenants_DefaultDueDays] CHECK ([DefaultDueDays] BETWEEN 0 AND 365)")]
    [InlineData("CONSTRAINT [CK_Tenants_BillPrefix] CHECK (LEN([BillPrefix]) BETWEEN 1 AND 3 AND [BillPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)")]
    [InlineData("CONSTRAINT [CK_Tenants_CreditNotePrefix] CHECK (LEN([CreditNotePrefix]) BETWEEN 1 AND 3 AND [CreditNotePrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)")]
    [InlineData("CONSTRAINT [CK_Tenants_PaymentPrefix] CHECK (LEN([PaymentPrefix]) BETWEEN 1 AND 3 AND [PaymentPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)")]
    [InlineData("CONSTRAINT [CK_Tenants_AdjustmentPrefix] CHECK (LEN([AdjustmentPrefix]) BETWEEN 1 AND 3 AND [AdjustmentPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)")]
    [InlineData("CONSTRAINT [CK_Tenants_RefundPrefix] CHECK (LEN([RefundPrefix]) BETWEEN 1 AND 3 AND [RefundPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2)")]
    public void The_table_has_the_documented_check_constraint(string constraint)
    {
        TenantsTable().ShouldContain(constraint, Case.Sensitive);
    }

    [Fact]
    public void The_status_constraint_lists_exactly_the_members_of_the_enum()
    {
        var names = string.Join(", ", Enum.GetNames<TenantStatus>().Select(name => $"'{name}'"));

        TenantsTable().ShouldContain($"CHECK ([Status] IN ({names}))", Case.Sensitive);
    }

    [Fact]
    public void No_column_has_a_database_default()
    {
        // Initial settings are set by Tenant.Create. A database default would silently replace an explicit zero.
        TenantsTable().ShouldNotContain(" DEFAULT ", Case.Sensitive);
    }

    [Fact]
    public void The_row_version_is_the_concurrency_token_and_the_domain_events_are_not_mapped()
    {
        var tenant = _context.Model.FindEntityType(typeof(Tenant))!;

        var rowVersion = tenant.FindProperty(nameof(Tenant.RowVersion))!;
        rowVersion.IsConcurrencyToken.ShouldBeTrue();
        rowVersion.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
        tenant.FindProperty(nameof(Tenant.DomainEvents)).ShouldBeNull();
        tenant.FindNavigation(nameof(Tenant.DomainEvents)).ShouldBeNull();
    }

    private string TenantsTable()
    {
        var start = _ddl.IndexOf("CREATE TABLE [Tenants]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "the model has no Tenants table");
        var end = _ddl.IndexOf(");", start, StringComparison.Ordinal);

        return _ddl[start..end];
    }
}
