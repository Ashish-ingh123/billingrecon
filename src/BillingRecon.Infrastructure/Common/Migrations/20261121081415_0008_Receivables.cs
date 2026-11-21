using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0008_Receivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversalOfPostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerPostings", x => x.Id);
                    table.CheckConstraint("CK_LedgerPostings_DocumentType", "[DocumentType] IN ('Bill', 'Payment', 'PaymentAllocation', 'CreditNote', 'CreditNoteAllocation', 'Adjustment', 'Refund')");
                    table.CheckConstraint("CK_LedgerPostings_Kind", "[Kind] IN ('BillPosted', 'PaymentRecorded', 'PaymentAllocated', 'AllocationReversed', 'PaymentReversed', 'CreditNotePosted', 'CreditApplied', 'CreditApplicationReversed', 'AdjustmentPosted', 'RefundPosted')");
                    table.ForeignKey(
                        name: "FK_LedgerPostings_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerPostings_ReversalOfPostingId",
                        column: x => x.ReversalOfPostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerPostings_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayerBalances",
                columns: table => new
                {
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Receivable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnappliedCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditAvailable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetOutstanding = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "[Receivable] - [UnappliedCash] - [CreditAvailable]", stored: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayerBalances", x => x.PayerId);
                    table.CheckConstraint("CK_PayerBalances_CreditAvailable", "[CreditAvailable] >= 0");
                    table.CheckConstraint("CK_PayerBalances_Receivable", "[Receivable] >= 0");
                    table.CheckConstraint("CK_PayerBalances_UnappliedCash", "[UnappliedCash] >= 0");
                    table.ForeignKey(
                        name: "FK_PayerBalances_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayerBalances_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<short>(type: "smallint", nullable: false),
                    Account = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SubjectType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_LedgerEntries_Account", "[Account] IN ('Receivable', 'UnappliedCash', 'CreditAvailable', 'Revenue', 'Cash', 'Adjustments')");
                    table.CheckConstraint("CK_LedgerEntries_Credit", "[Credit] >= 0");
                    table.CheckConstraint("CK_LedgerEntries_Debit", "[Debit] >= 0");
                    table.CheckConstraint("CK_LedgerEntries_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_LedgerEntries_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Debit] = 0 AND [Credit] > 0)");
                    table.CheckConstraint("CK_LedgerEntries_Subject", "([Account] = 'Receivable' AND [SubjectType] = 'Bill' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'UnappliedCash' AND [SubjectType] = 'Payment' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'CreditAvailable' AND [SubjectType] = 'CreditNote' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] IN ('Revenue', 'Cash', 'Adjustments') AND [SubjectType] IS NULL AND [SubjectId] IS NULL AND [PayerId] IS NULL)");
                    table.CheckConstraint("CK_LedgerEntries_SubjectType", "[SubjectType] IN ('Bill', 'Payment', 'CreditNote')");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Payers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_PostingId",
                        column: x => x.PostingId,
                        principalTable: "LedgerPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_PayerId_Account",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "PayerId", "Account" })
                .Annotation("SqlServer:Include", new[] { "Debit", "Credit" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_SubjectType_SubjectId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "SubjectType", "SubjectId" })
                .Annotation("SqlServer:Include", new[] { "Account", "Debit", "Credit" });

            migrationBuilder.CreateIndex(
                name: "UX_LedgerEntries_TenantId_PostingId_LineNo",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "PostingId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerPostings_TenantId_BusinessDate",
                table: "LedgerPostings",
                columns: new[] { "TenantId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerPostings_TenantId_DocumentType_DocumentId",
                table: "LedgerPostings",
                columns: new[] { "TenantId", "DocumentType", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerPostings_TenantId_PayerId",
                table: "LedgerPostings",
                columns: new[] { "TenantId", "PayerId" });

            migrationBuilder.Sql("INSERT INTO [PayerBalances] ([PayerId], [TenantId], [Receivable], [UnappliedCash], [CreditAvailable], [UpdatedAtUtc]) SELECT p.[Id], p.[TenantId], 0, 0, 0, SYSUTCDATETIME() FROM [Payers] AS p WHERE NOT EXISTS (SELECT 1 FROM [PayerBalances] AS b WHERE b.[PayerId] = p.[Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "PayerBalances");

            migrationBuilder.DropTable(
                name: "LedgerPostings");
        }
    }
}
