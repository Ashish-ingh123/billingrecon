using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0023_OpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrefixChanges_DocumentType",
                table: "PrefixChanges");

            migrationBuilder.DropIndex(
                name: "UX_Payments_TenantId_FiscalYear_SequenceNumber",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NumberSeries_DocumentType",
                table: "NumberSeries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerPostings_Kind",
                table: "LedgerPostings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_Account",
                table: "LedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_Subject",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "UX_Bills_TenantId_FiscalYear_SequenceNumber",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.AddColumn<string>(
                name: "LegacyNumber",
                table: "Payments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Payments",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "LegacyReference",
                table: "Payers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyNumber",
                table: "Bills",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Bills",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "OpeningBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CutOverDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BillsCsv = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdvancesCsv = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TrialBalanceCsv = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Receivables = table.Column<int>(type: "int", nullable: false),
                    ReceivableTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Advances = table.Column<int>(type: "int", nullable: false),
                    AdvanceTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBatches", x => x.Id);
                    table.CheckConstraint("CK_OpeningBatches_Counts", "[Receivables] >= 0 AND [Advances] >= 0 AND [Receivables] + [Advances] >= 1");
                    table.CheckConstraint("CK_OpeningBatches_Decided", "([Status] = 'PendingApproval' AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'PendingApproval' AND [DecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_OpeningBatches_Status", "[Status] IN ('PendingApproval', 'Approved', 'Rejected')");
                    table.CheckConstraint("CK_OpeningBatches_Totals", "[ReceivableTotal] >= 0 AND [AdvanceTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_OpeningBatches_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBatches_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBatches_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrefixChanges_DocumentType",
                table: "PrefixChanges",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund', 'OpeningReceivable', 'OpeningAdvance')");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_LegacyNumber",
                table: "Payments",
                columns: new[] { "TenantId", "LegacyNumber" },
                unique: true,
                filter: "[LegacyNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_Origin_FiscalYear_SequenceNumber",
                table: "Payments",
                columns: new[] { "TenantId", "Origin", "FiscalYear", "SequenceNumber" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_LegacyNumberIffOpening",
                table: "Payments",
                sql: "([Origin] = 'Standard' AND [LegacyNumber] IS NULL) OR ([Origin] = 'Opening' AND [LegacyNumber] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Origin",
                table: "Payments",
                sql: "[Origin] IN ('Standard', 'Opening')");

            migrationBuilder.CreateIndex(
                name: "UX_Payers_TenantId_LegacyReference",
                table: "Payers",
                columns: new[] { "TenantId", "LegacyReference" },
                unique: true,
                filter: "[LegacyReference] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NumberSeries_DocumentType",
                table: "NumberSeries",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund', 'OpeningReceivable', 'OpeningAdvance')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerPostings_Kind",
                table: "LedgerPostings",
                sql: "[Kind] IN ('BillPosted', 'PaymentRecorded', 'PaymentAllocated', 'AllocationReversed', 'PaymentReversed', 'CreditNotePosted', 'CreditApplied', 'CreditApplicationReversed', 'AdjustmentPosted', 'RefundPosted', 'OpeningReceivablePosted', 'OpeningAdvancePosted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_Account",
                table: "LedgerEntries",
                sql: "[Account] IN ('Receivable', 'UnappliedCash', 'CreditAvailable', 'Revenue', 'Cash', 'Adjustments', 'OpeningBalances')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_Subject",
                table: "LedgerEntries",
                sql: "([Account] = 'Receivable' AND [SubjectType] = 'Bill' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'UnappliedCash' AND [SubjectType] = 'Payment' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'CreditAvailable' AND [SubjectType] = 'CreditNote' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] IN ('Revenue', 'Cash', 'Adjustments', 'OpeningBalances') AND [SubjectType] IS NULL AND [SubjectId] IS NULL AND [PayerId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_Bills_TenantId_LegacyNumber",
                table: "Bills",
                columns: new[] { "TenantId", "LegacyNumber" },
                unique: true,
                filter: "[LegacyNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Bills_TenantId_Origin_FiscalYear_SequenceNumber",
                table: "Bills",
                columns: new[] { "TenantId", "Origin", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bills_LegacyNumberIffOpening",
                table: "Bills",
                sql: "([Origin] = 'Standard' AND [LegacyNumber] IS NULL) OR ([Origin] = 'Opening' AND [LegacyNumber] IS NOT NULL AND [Number] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bills_Origin",
                table: "Bills",
                sql: "[Origin] IN ('Standard', 'Opening')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests",
                sql: "[Action] IN ('Post', 'Reverse', 'ChangePrefix', 'BringForward')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'PrefixChange', 'OpeningBatch')");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBatches_TenantId_CreatedAtUtc",
                table: "OpeningBatches",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OpeningBatches_TenantId_ContentHash",
                table: "OpeningBatches",
                columns: new[] { "TenantId", "ContentHash" },
                unique: true,
                filter: "[Status] <> 'Rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpeningBatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PrefixChanges_DocumentType",
                table: "PrefixChanges");

            migrationBuilder.DropIndex(
                name: "UX_Payments_TenantId_LegacyNumber",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Payments_TenantId_Origin_FiscalYear_SequenceNumber",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_LegacyNumberIffOpening",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Origin",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Payers_TenantId_LegacyReference",
                table: "Payers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NumberSeries_DocumentType",
                table: "NumberSeries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerPostings_Kind",
                table: "LedgerPostings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_Account",
                table: "LedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_Subject",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "UX_Bills_TenantId_LegacyNumber",
                table: "Bills");

            migrationBuilder.DropIndex(
                name: "UX_Bills_TenantId_Origin_FiscalYear_SequenceNumber",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bills_LegacyNumberIffOpening",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bills_Origin",
                table: "Bills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "LegacyNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LegacyReference",
                table: "Payers");

            migrationBuilder.DropColumn(
                name: "LegacyNumber",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Bills");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrefixChanges_DocumentType",
                table: "PrefixChanges",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund')");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_FiscalYear_SequenceNumber",
                table: "Payments",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NumberSeries_DocumentType",
                table: "NumberSeries",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerPostings_Kind",
                table: "LedgerPostings",
                sql: "[Kind] IN ('BillPosted', 'PaymentRecorded', 'PaymentAllocated', 'AllocationReversed', 'PaymentReversed', 'CreditNotePosted', 'CreditApplied', 'CreditApplicationReversed', 'AdjustmentPosted', 'RefundPosted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_Account",
                table: "LedgerEntries",
                sql: "[Account] IN ('Receivable', 'UnappliedCash', 'CreditAvailable', 'Revenue', 'Cash', 'Adjustments')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_Subject",
                table: "LedgerEntries",
                sql: "([Account] = 'Receivable' AND [SubjectType] = 'Bill' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'UnappliedCash' AND [SubjectType] = 'Payment' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] = 'CreditAvailable' AND [SubjectType] = 'CreditNote' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) OR ([Account] IN ('Revenue', 'Cash', 'Adjustments') AND [SubjectType] IS NULL AND [SubjectId] IS NULL AND [PayerId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_Bills_TenantId_FiscalYear_SequenceNumber",
                table: "Bills",
                columns: new[] { "TenantId", "FiscalYear", "SequenceNumber" },
                unique: true,
                filter: "[SequenceNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests",
                sql: "[Action] IN ('Post', 'Reverse', 'ChangePrefix')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'PrefixChange')");
        }
    }
}
