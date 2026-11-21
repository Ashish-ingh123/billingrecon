using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0015_PaymentReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversalPostingId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversedAtUtc",
                table: "Payments",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalPostingId",
                table: "PaymentAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "PaymentAllocations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversedAtUtc",
                table: "PaymentAllocations",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                table: "PaymentAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_ReversedIffReversed",
                table: "Payments",
                sql: "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL AND [UnappliedAmount] = 0) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAllocations_ReversedIffReversed",
                table: "PaymentAllocations",
                sql: "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_ReversalPostingId",
                table: "PaymentAllocations",
                column: "ReversalPostingId",
                principalTable: "LedgerPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_ReversalPostingId",
                table: "Payments",
                column: "ReversalPostingId",
                principalTable: "LedgerPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_ReversalPostingId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_ReversalPostingId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_ReversedIffReversed",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAllocations_ReversedIffReversed",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ReversalPostingId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversalPostingId",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ReversedByUserId",
                table: "PaymentAllocations");
        }
    }
}
