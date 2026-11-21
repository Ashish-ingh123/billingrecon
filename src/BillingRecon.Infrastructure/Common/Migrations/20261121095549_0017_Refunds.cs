using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0017_Refunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SourcePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Refunds_Method", "[Method] IN ('Cash', 'Card', 'UPI', 'BankTransfer', 'Cheque')");
                    table.CheckConstraint("CK_Refunds_NumberIffPosted", "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL AND [PostingId] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [PostingId] IS NOT NULL)");
                    table.CheckConstraint("CK_Refunds_NumberParts", "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
                    table.CheckConstraint("CK_Refunds_OneSource", "([SourceKind] = 'Payment' AND [SourcePaymentId] IS NOT NULL AND [SourceCreditNoteId] IS NULL) OR ([SourceKind] = 'CreditNote' AND [SourceCreditNoteId] IS NOT NULL AND [SourcePaymentId] IS NULL)");
                    table.CheckConstraint("CK_Refunds_Reference", "[Method] = 'Cash' OR [Reference] IS NOT NULL");
                    table.CheckConstraint("CK_Refunds_SourceKind", "[SourceKind] IN ('Payment', 'CreditNote')");
                    table.CheckConstraint("CK_Refunds_Status", "[Status] IN ('Draft', 'PendingApproval', 'Posted', 'Voided')");
                    table.CheckConstraint("CK_Refunds_VoidedIffVoided", "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Refunds_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_PostingId",
                        column: x => x.PostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_SourceCreditNoteId",
                        column: x => x.SourceCreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_SourcePaymentId",
                        column: x => x.SourcePaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_PayerId",
                table: "Refunds",
                columns: new[] { "TenantId", "PayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_SourceCreditNoteId",
                table: "Refunds",
                columns: new[] { "TenantId", "SourceCreditNoteId" })
                .Annotation("SqlServer:Include", new[] { "Amount", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_SourcePaymentId",
                table: "Refunds",
                columns: new[] { "TenantId", "SourcePaymentId" })
                .Annotation("SqlServer:Include", new[] { "Amount", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_TenantId_FiscalYear_SequenceNumber",
                table: "Refunds",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_TenantId_Number",
                table: "Refunds",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Refunds");
        }
    }
}
