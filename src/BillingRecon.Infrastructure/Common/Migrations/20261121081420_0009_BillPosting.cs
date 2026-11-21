using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0009_BillPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalYear",
                table: "Bills",
                type: "char(7)",
                unicode: false,
                fixedLength: true,
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Bills",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PostedAtUtc",
                table: "Bills",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostedByUserId",
                table: "Bills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceNumber",
                table: "Bills",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bills_TenantId_FiscalYear_SequenceNumber",
                table: "Bills",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Bills_TenantId_Number",
                table: "Bills",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bills_NumberIffPosted",
                table: "Bills",
                sql: "([Status] IN ('Draft', 'PendingApproval', 'Voided') AND [Number] IS NULL AND [PostedAtUtc] IS NULL) OR ([Status] IN ('Posted', 'PartiallyPaid', 'Settled') AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bills_NumberParts",
                table: "Bills",
                sql: "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bills_StatusMatchesBalance",
                table: "Bills",
                sql: "([Status] <> 'Posted' OR ([BalanceDue] = [Total] AND [Total] > 0)) AND ([Status] <> 'PartiallyPaid' OR ([BalanceDue] > 0 AND [BalanceDue] < [Total])) AND ([Status] <> 'Settled' OR [BalanceDue] = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Bills_TenantId_FiscalYear_SequenceNumber",
                table: "Bills");

            migrationBuilder.DropIndex(
                name: "UX_Bills_TenantId_Number",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bills_NumberIffPosted",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bills_NumberParts",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bills_StatusMatchesBalance",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "FiscalYear",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "PostedAtUtc",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "Bills");
        }
    }
}
