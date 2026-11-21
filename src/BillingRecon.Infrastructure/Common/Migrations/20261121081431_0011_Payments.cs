using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0011_Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    FiscalYear = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnappliedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Payments_Method", "[Method] IN ('Cash', 'Card', 'UPI', 'BankTransfer', 'Cheque')");
                    table.CheckConstraint("CK_Payments_Reference", "[Method] = 'Cash' OR [Reference] IS NOT NULL");
                    table.CheckConstraint("CK_Payments_SequenceNumber", "[SequenceNumber] >= 1");
                    table.CheckConstraint("CK_Payments_Status", "[Status] IN ('Posted', 'PendingReversal', 'Reversed')");
                    table.CheckConstraint("CK_Payments_UnappliedAmount", "[UnappliedAmount] BETWEEN 0 AND [Amount]");
                    table.ForeignKey(
                        name: "FK_Payments_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_PostingId",
                        column: x => x.PostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_PayerId_Status",
                table: "Payments",
                columns: new[] { "TenantId", "PayerId", "Status" })
                .Annotation("SqlServer:Include", new[] { "UnappliedAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_PaymentDate",
                table: "Payments",
                columns: new[] { "TenantId", "PaymentDate" })
                .Annotation("SqlServer:Include", new[] { "Method", "Amount", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_FiscalYear_SequenceNumber",
                table: "Payments",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_Number",
                table: "Payments",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
