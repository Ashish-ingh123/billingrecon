using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0031_SecondStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalHoldUntilUtc",
                table: "Users",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodeHashes",
                table: "Users",
                type: "varchar(700)",
                unicode: false,
                maxLength: 700,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SecondStepEnrolledAtUtc",
                table: "Users",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondStepFailures",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SecondStepLastStep",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondStepPendingSecret",
                table: "Users",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondStepSecret",
                table: "Users",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_SecondStepFailures",
                table: "Users",
                sql: "[SecondStepFailures] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_SecondStepFailures",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalHoldUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeHashes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondStepEnrolledAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondStepFailures",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondStepLastStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondStepPendingSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondStepSecret",
                table: "Users");
        }
    }
}
