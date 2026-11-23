using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0020_DatabaseRoles : Migration
    {
        // Scaffolded empty (the model did not change) and written by hand, as ADR 0034 allows: the database role of the
        // application and its grants, which the model cannot express (docs/database.md section 7). The logins, their
        // passwords and their database users are made per environment (docs/runbook.md section 10), never here.
        // DatabasePermissionsOnSqlServerTests compares every table's grants with the markers of its entities.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Owned by dbo, not by the login that migrates: the migrator's user can be dropped and the role stays.
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'billingrecon_application') IS NULL EXEC(N'CREATE ROLE [billingrecon_application] AUTHORIZATION [dbo]');");

            // Every table of the schema, including the ones later migrations add: read, add, change; never delete.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON SCHEMA::[dbo] TO [billingrecon_application];");

            // Append-only (IAppendOnly; BR-402, BR-702): the ledger and the audit trail are never changed or deleted.
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[LedgerPostings] TO [billingrecon_application];");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[LedgerEntries] TO [billingrecon_application];");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[AuditRecords] TO [billingrecon_application];");

            // Deleted rows that are not financial records (IHardDeletable; CLAUDE.md section 4, rule 10): the lines of a
            // draft that are replaced, the roles of a user that are replaced, the key of a request that failed.
            migrationBuilder.Sql("GRANT DELETE ON [dbo].[BillLines] TO [billingrecon_application];");
            migrationBuilder.Sql("GRANT DELETE ON [dbo].[CreditNoteLines] TO [billingrecon_application];");
            migrationBuilder.Sql("GRANT DELETE ON [dbo].[UserRoles] TO [billingrecon_application];");
            migrationBuilder.Sql("GRANT DELETE ON [dbo].[IdempotencyKeys] TO [billingrecon_application];");

            // Read only: the roles, which migration 0018 wrote, and the history of the migrations, which the readiness
            // check reads.
            migrationBuilder.Sql("DENY INSERT, UPDATE, DELETE ON [dbo].[Roles] TO [billingrecon_application];");
            migrationBuilder.Sql("DENY INSERT, UPDATE, DELETE ON [dbo].[__EFMigrationsHistory] TO [billingrecon_application];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The role's members leave it first; dropping the role drops its grants and denials with it.
            migrationBuilder.Sql(
                """
                DECLARE @members nvarchar(max) = N'';
                SELECT @members += N'ALTER ROLE [billingrecon_application] DROP MEMBER ' + QUOTENAME(m.[name]) + N';'
                FROM   sys.database_role_members AS rm
                JOIN   sys.database_principals AS r ON r.principal_id = rm.role_principal_id
                JOIN   sys.database_principals AS m ON m.principal_id = rm.member_principal_id
                WHERE  r.[name] = N'billingrecon_application';
                EXEC (@members);
                """);
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'billingrecon_application') IS NOT NULL DROP ROLE [billingrecon_application];");
        }
    }
}
