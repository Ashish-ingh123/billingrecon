using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The migrations, checked without a database: they exist, they are complete (the model has no change that is
/// missing from them), and the script they produce creates the Tenants table. Applying them to a real SQL Server
/// is covered by the Testcontainers tests.
/// </summary>
public sealed class MigrationsTests : IDisposable
{
    private const string HowToAdd =
        "Run: dotnet ef migrations add <name> --project src/BillingRecon.Infrastructure --output-dir Common/Migrations (docs/runbook.md, section 3).";

    private readonly BillingReconDbContext _context = TestContexts.CreateProduction();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void The_first_migration_is_0001_Tenants()
    {
        var migrations = _context.Database.GetMigrations().ToArray();

        migrations.ShouldNotBeEmpty($"There is no migration yet. {HowToAdd.Replace("<name>", "0001_Tenants", StringComparison.Ordinal)}");
        migrations[0].ShouldEndWith("_0001_Tenants");
    }

    [Fact]
    public void The_model_has_no_change_that_is_missing_from_the_migrations()
    {
        _context.Database.HasPendingModelChanges().ShouldBeFalse(
            $"The model differs from the last migration: a migration is missing. {HowToAdd}");
    }

    [Fact]
    public void The_migrations_create_the_tenants_table_as_documented()
    {
        var script = _context.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("CREATE TABLE [Tenants]", customMessage: $"The migrations do not create the Tenants table. {HowToAdd}");
        script.ShouldContain("[Code] varchar(20) NOT NULL", Case.Sensitive);
        script.ShouldContain("[PostingApprovalThreshold] decimal(18,2) NULL", Case.Sensitive);
        script.ShouldContain("[CreatedAtUtc] datetimeoffset(3) NOT NULL", Case.Sensitive);
        script.ShouldContain("[RowVersion] rowversion NOT NULL", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])", Case.Sensitive);
        script.ShouldContain("CONSTRAINT [CK_Tenants_Status] CHECK ([Status] IN ('Active', 'Suspended'))", Case.Sensitive);
        script.ShouldContain("CREATE UNIQUE INDEX [UX_Tenants_Code] ON [Tenants] ([Code])", Case.Sensitive);
    }
}
