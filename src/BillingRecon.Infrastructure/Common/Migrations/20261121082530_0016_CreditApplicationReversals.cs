using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0016_CreditApplicationReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNoteAllocations_Status",
                table: "CreditNoteAllocations");

            migrationBuilder.AddColumn<bool>(
                name: "AppliedAtPosting",
                table: "CreditNoteAllocations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalPostingId",
                table: "CreditNoteAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "CreditNoteAllocations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversedAtUtc",
                table: "CreditNoteAllocations",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                table: "CreditNoteAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNoteAllocations_AtPostingStaysActive",
                table: "CreditNoteAllocations",
                sql: "[AppliedAtPosting] = 0 OR [Status] = 'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNoteAllocations_ReversedIffReversed",
                table: "CreditNoteAllocations",
                sql: "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNoteAllocations_Status",
                table: "CreditNoteAllocations",
                sql: "[Status] IN ('Active', 'PendingReversal', 'Reversed')");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteAllocations_ReversalPostingId",
                table: "CreditNoteAllocations",
                column: "ReversalPostingId",
                principalTable: "LedgerPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteAllocations_ReversalPostingId",
                table: "CreditNoteAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNoteAllocations_AtPostingStaysActive",
                table: "CreditNoteAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNoteAllocations_ReversedIffReversed",
                table: "CreditNoteAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNoteAllocations_Status",
                table: "CreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "AppliedAtPosting",
                table: "CreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "ReversalPostingId",
                table: "CreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "CreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                table: "CreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "ReversedByUserId",
                table: "CreditNoteAllocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNoteAllocations_Status",
                table: "CreditNoteAllocations",
                sql: "[Status] IN ('Active')");
        }
    }
}
