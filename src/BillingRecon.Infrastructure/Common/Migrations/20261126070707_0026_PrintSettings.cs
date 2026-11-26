using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0026_PrintSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Paper = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    MarginTopMm = table.Column<int>(type: "int", nullable: false),
                    MarginRightMm = table.Column<int>(type: "int", nullable: false),
                    MarginBottomMm = table.Column<int>(type: "int", nullable: false),
                    MarginLeftMm = table.Column<int>(type: "int", nullable: false),
                    LogoPosition = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LogoSize = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PrePrintedStationery = table.Column<bool>(type: "bit", nullable: false),
                    StationeryTopMm = table.Column<int>(type: "int", nullable: false),
                    ShowPatientContact = table.Column<bool>(type: "bit", nullable: false),
                    ShowPayer = table.Column<bool>(type: "bit", nullable: false),
                    ShowSacColumn = table.Column<bool>(type: "bit", nullable: false),
                    ShowTaxSplit = table.Column<bool>(type: "bit", nullable: false),
                    ShowAmountInWords = table.Column<bool>(type: "bit", nullable: false),
                    ShowSignatureBlock = table.Column<bool>(type: "bit", nullable: false),
                    ShowCashierName = table.Column<bool>(type: "bit", nullable: false),
                    FooterLine = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Terms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintSettings", x => x.Id);
                    table.CheckConstraint("CK_PrintSettings_BillPaper", "[DocumentType] <> 'Bill' OR [Paper] <> 'Roll80'");
                    table.CheckConstraint("CK_PrintSettings_BillShowsTax", "[DocumentType] <> 'Bill' OR ([ShowSacColumn] = 1 AND [ShowTaxSplit] = 1)");
                    table.CheckConstraint("CK_PrintSettings_DocumentType", "[DocumentType] IN ('Bill', 'Payment', 'Refund')");
                    table.CheckConstraint("CK_PrintSettings_LogoPosition", "[LogoPosition] IN ('Left', 'Centre', 'Right')");
                    table.CheckConstraint("CK_PrintSettings_LogoSize", "[LogoSize] IN ('Small', 'Medium', 'Large')");
                    table.CheckConstraint("CK_PrintSettings_MarginBottomMm", "[MarginBottomMm] BETWEEN 5 AND 25");
                    table.CheckConstraint("CK_PrintSettings_MarginLeftMm", "[MarginLeftMm] BETWEEN 5 AND 25");
                    table.CheckConstraint("CK_PrintSettings_MarginRightMm", "[MarginRightMm] BETWEEN 5 AND 25");
                    table.CheckConstraint("CK_PrintSettings_MarginTopMm", "[MarginTopMm] BETWEEN 5 AND 25");
                    table.CheckConstraint("CK_PrintSettings_Paper", "[Paper] IN ('A4', 'A5', 'Roll80')");
                    table.CheckConstraint("CK_PrintSettings_StationeryTopMm", "[StationeryTopMm] BETWEEN 0 AND 80");
                    table.ForeignKey(
                        name: "FK_PrintSettings_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintSettings_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintSettings_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PrintSettings_TenantId_DocumentType",
                table: "PrintSettings",
                columns: new[] { "TenantId", "DocumentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintSettings");
        }
    }
}
