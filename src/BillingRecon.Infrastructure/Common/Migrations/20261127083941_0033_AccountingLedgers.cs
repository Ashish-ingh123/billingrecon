using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0033_AccountingLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Receivable = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnappliedCash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreditAvailable = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Revenue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaxOutput = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Adjustments = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OpeningBalances = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Card = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Upi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BankTransfer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cheque = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingLedgers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingLedgers_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingLedgers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AccountingLedgers_TenantId",
                table: "AccountingLedgers",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingLedgers");
        }
    }
}
