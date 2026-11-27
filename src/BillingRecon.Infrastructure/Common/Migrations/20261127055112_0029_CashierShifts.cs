using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0029_CashierShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "LedgerPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashierShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CashDocuments = table.Column<int>(type: "int", nullable: false),
                    Denominations = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: true),
                    LooseChange = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CountedCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DifferenceReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CountedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashierShifts", x => x.Id);
                    table.CheckConstraint("CK_CashierShifts_CashDocuments", "[CashDocuments] >= 0");
                    table.CheckConstraint("CK_CashierShifts_ClosedIffClosed", "([Status] = 'Closed' AND [ClosedAtUtc] IS NOT NULL) OR ([Status] <> 'Closed' AND [ClosedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_CashierShifts_CountedIffNotOpen", "([Status] = 'Open' AND [Denominations] IS NULL AND [LooseChange] IS NULL AND [CountedCash] IS NULL AND [ExpectedCash] IS NULL AND [Difference] IS NULL AND [DifferenceReason] IS NULL AND [CountedAtUtc] IS NULL) OR ([Status] <> 'Open' AND [Denominations] IS NOT NULL AND [LooseChange] IS NOT NULL AND [CountedCash] IS NOT NULL AND [ExpectedCash] IS NOT NULL AND [Difference] IS NOT NULL AND [CountedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_CashierShifts_Difference", "[Difference] IS NULL OR [Difference] = [CountedCash] - [ExpectedCash]");
                    table.CheckConstraint("CK_CashierShifts_LooseChange", "[LooseChange] IS NULL OR ([LooseChange] >= 0 AND [LooseChange] < 1)");
                    table.CheckConstraint("CK_CashierShifts_OpeningFloat", "[OpeningFloat] >= 0");
                    table.CheckConstraint("CK_CashierShifts_PendingHasDifference", "[Status] <> 'PendingApproval' OR [Difference] <> 0");
                    table.CheckConstraint("CK_CashierShifts_ReasonIffDifference", "[Difference] IS NULL OR ([Difference] = 0 AND [DifferenceReason] IS NULL) OR ([Difference] <> 0 AND [DifferenceReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CashierShifts_Status", "[Status] IN ('Open', 'PendingApproval', 'Closed')");
                    table.ForeignKey(
                        name: "FK_CashierShifts_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashierShifts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashierShifts_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashierShifts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerPostings_TenantId_ShiftId",
                table: "LedgerPostings",
                columns: new[] { "TenantId", "ShiftId" },
                filter: "[ShiftId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests",
                sql: "[Action] IN ('Post', 'Reverse', 'ChangePrefix', 'BringForward', 'Close')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'PrefixChange', 'OpeningBatch', 'CashierShift')");

            migrationBuilder.CreateIndex(
                name: "IX_CashierShifts_TenantId_BusinessDate",
                table: "CashierShifts",
                columns: new[] { "TenantId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "UX_CashierShifts_TenantId_CashierUserId_Unclosed",
                table: "CashierShifts",
                columns: new[] { "TenantId", "CashierUserId" },
                unique: true,
                filter: "[Status] <> 'Closed'");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerPostings_ShiftId",
                table: "LedgerPostings",
                column: "ShiftId",
                principalTable: "CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerPostings_ShiftId",
                table: "LedgerPostings");

            migrationBuilder.DropTable(
                name: "CashierShifts");

            migrationBuilder.DropIndex(
                name: "IX_LedgerPostings_TenantId_ShiftId",
                table: "LedgerPostings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "LedgerPostings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_Action",
                table: "ApprovalRequests",
                sql: "[Action] IN ('Post', 'Reverse', 'ChangePrefix', 'BringForward')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'PrefixChange', 'OpeningBatch')");
        }
    }
}
