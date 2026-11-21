using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0013_CreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Number = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    FiscalYear = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: true),
                    SequenceNumber = table.Column<int>(type: "int", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_AvailableAmount", "[AvailableAmount] BETWEEN 0 AND [Total]");
                    table.CheckConstraint("CK_CreditNotes_DiscountTotal", "[DiscountTotal] >= 0");
                    table.CheckConstraint("CK_CreditNotes_NumberIffPosted", "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [Total] > 0)");
                    table.CheckConstraint("CK_CreditNotes_NumberParts", "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
                    table.CheckConstraint("CK_CreditNotes_Status", "[Status] IN ('Draft', 'PendingApproval', 'Posted', 'Voided')");
                    table.CheckConstraint("CK_CreditNotes_SubTotal", "[SubTotal] >= 0");
                    table.CheckConstraint("CK_CreditNotes_TaxTotal", "[TaxTotal] >= 0");
                    table.CheckConstraint("CK_CreditNotes_Total", "[Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
                    table.CheckConstraint("CK_CreditNotes_VoidedIffVoided", "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_CreditNotes_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AllocatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    AllocatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteAllocations", x => x.Id);
                    table.CheckConstraint("CK_CreditNoteAllocations_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_CreditNoteAllocations_Status", "[Status] IN ('Active')");
                    table.ForeignKey(
                        name: "FK_CreditNoteAllocations_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteAllocations_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteAllocations_PostingId",
                        column: x => x.PostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteAllocations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    BillLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_CreditNoteLines", x => x.Id);
                    table.CheckConstraint("CK_CreditNoteLines_DiscountAmount", "[DiscountAmount] BETWEEN 0 AND [LineAmount]");
                    table.CheckConstraint("CK_CreditNoteLines_LineAmount", "[LineAmount] >= 0");
                    table.CheckConstraint("CK_CreditNoteLines_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_CreditNoteLines_LineTotal", "[LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount]");
                    table.CheckConstraint("CK_CreditNoteLines_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_CreditNoteLines_TaxAmount", "[TaxAmount] >= 0");
                    table.CheckConstraint("CK_CreditNoteLines_TaxRatePercent", "[TaxRatePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_CreditNoteLines_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_BillLineId",
                        column: x => x.BillLineId,
                        principalTable: "BillLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteAllocations_TenantId_BillId",
                table: "CreditNoteAllocations",
                columns: new[] { "TenantId", "BillId" })
                .Annotation("SqlServer:Include", new[] { "Amount", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteAllocations_TenantId_CreditNoteId",
                table: "CreditNoteAllocations",
                columns: new[] { "TenantId", "CreditNoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_TenantId_BillLineId",
                table: "CreditNoteLines",
                columns: new[] { "TenantId", "BillLineId" })
                .Annotation("SqlServer:Include", new[] { "LineTotal" });

            migrationBuilder.CreateIndex(
                name: "UX_CreditNoteLines_TenantId_CreditNoteId_LineNo",
                table: "CreditNoteLines",
                columns: new[] { "TenantId", "CreditNoteId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_BillId_Status",
                table: "CreditNotes",
                columns: new[] { "TenantId", "BillId", "Status" })
                .Annotation("SqlServer:Include", new[] { "Total" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_PatientId",
                table: "CreditNotes",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_PayerId_Status",
                table: "CreditNotes",
                columns: new[] { "TenantId", "PayerId", "Status" })
                .Annotation("SqlServer:Include", new[] { "AvailableAmount" });

            migrationBuilder.CreateIndex(
                name: "UX_CreditNotes_TenantId_FiscalYear_SequenceNumber",
                table: "CreditNotes",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CreditNotes_TenantId_Number",
                table: "CreditNotes",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNoteAllocations");

            migrationBuilder.DropTable(
                name: "CreditNoteLines");

            migrationBuilder.DropTable(
                name: "CreditNotes");
        }
    }
}
