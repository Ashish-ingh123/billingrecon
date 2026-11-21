using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0004_Bills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EncounterReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bills", x => x.Id);
                    table.CheckConstraint("CK_Bills_BalanceDue", "[BalanceDue] BETWEEN 0 AND [Total]");
                    table.CheckConstraint("CK_Bills_DiscountTotal", "[DiscountTotal] >= 0");
                    table.CheckConstraint("CK_Bills_DueDate", "[DueDate] >= [BillDate]");
                    table.CheckConstraint("CK_Bills_Status", "[Status] IN ('Draft', 'Voided', 'Posted', 'PartiallyPaid', 'Settled', 'PendingApproval')");
                    table.CheckConstraint("CK_Bills_SubTotal", "[SubTotal] >= 0");
                    table.CheckConstraint("CK_Bills_TaxTotal", "[TaxTotal] >= 0");
                    table.CheckConstraint("CK_Bills_Total", "[Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
                    table.CheckConstraint("CK_Bills_VoidedIffVoided", "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Bills_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bills_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bills_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ServiceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    LineAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillLines", x => x.Id);
                    table.CheckConstraint("CK_BillLines_DiscountAmount", "[DiscountAmount] BETWEEN 0 AND [LineAmount]");
                    table.CheckConstraint("CK_BillLines_LineAmount", "[LineAmount] >= 0");
                    table.CheckConstraint("CK_BillLines_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_BillLines_LineTotal", "[LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount]");
                    table.CheckConstraint("CK_BillLines_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_BillLines_TaxAmount", "[TaxAmount] >= 0");
                    table.CheckConstraint("CK_BillLines_TaxRatePercent", "[TaxRatePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_BillLines_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_BillLines_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillLines_ServiceItemId",
                        column: x => x.ServiceItemId,
                        principalTable: "ServiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillLines_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_BillLines_TenantId_BillId_LineNo",
                table: "BillLines",
                columns: new[] { "TenantId", "BillId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_BillDate",
                table: "Bills",
                columns: new[] { "TenantId", "BillDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_PatientId",
                table: "Bills",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_PayerId_Status",
                table: "Bills",
                columns: new[] { "TenantId", "PayerId", "Status" })
                .Annotation("SqlServer:Include", new[] { "BalanceDue", "Total" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillLines");

            migrationBuilder.DropTable(
                name: "Bills");
        }
    }
}
