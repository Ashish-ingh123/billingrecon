using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0014_Adjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Number = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    FiscalYear = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: true),
                    SequenceNumber = table.Column<int>(type: "int", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjustments", x => x.Id);
                    table.CheckConstraint("CK_Adjustments_Amount", "[Amount] > 0 AND ([Kind] <> 'Rounding' OR [Amount] < 1.00)");
                    table.CheckConstraint("CK_Adjustments_Kind", "[Kind] IN ('Discount', 'WriteOff', 'Rounding')");
                    table.CheckConstraint("CK_Adjustments_NumberIffPosted", "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL AND [PostingId] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [PostingId] IS NOT NULL)");
                    table.CheckConstraint("CK_Adjustments_NumberParts", "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
                    table.CheckConstraint("CK_Adjustments_Status", "[Status] IN ('Draft', 'PendingApproval', 'Posted', 'Voided')");
                    table.CheckConstraint("CK_Adjustments_VoidedIffVoided", "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Adjustments_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adjustments_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adjustments_PostingId",
                        column: x => x.PostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adjustments_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_TenantId_BillId_Status",
                table: "Adjustments",
                columns: new[] { "TenantId", "BillId", "Status" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_TenantId_PayerId",
                table: "Adjustments",
                columns: new[] { "TenantId", "PayerId" });

            migrationBuilder.CreateIndex(
                name: "UX_Adjustments_TenantId_FiscalYear_SequenceNumber",
                table: "Adjustments",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Adjustments_TenantId_Number",
                table: "Adjustments",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Adjustments");
        }
    }
}
