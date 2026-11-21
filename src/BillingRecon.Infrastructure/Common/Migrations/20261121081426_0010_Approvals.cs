using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0010_Approvals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    MakerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MakerComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CheckerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CheckerComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.CheckConstraint("CK_ApprovalRequests_Action", "[Action] IN ('Post', 'Reverse', 'ChangePrefix')");
                    table.CheckConstraint("CK_ApprovalRequests_CheckerIsNotMaker", "[CheckerUserId] IS NULL OR [CheckerUserId] <> [MakerUserId]");
                    table.CheckConstraint("CK_ApprovalRequests_Decided", "([Status] = 'Pending' AND [CheckerUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'Pending' AND [CheckerUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ApprovalRequests_DocumentType", "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'NumberSeries')");
                    table.CheckConstraint("CK_ApprovalRequests_RejectionHasComment", "[Status] <> 'Rejected' OR [CheckerComment] IS NOT NULL");
                    table.CheckConstraint("CK_ApprovalRequests_Status", "[Status] IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_Status",
                table: "Bills",
                columns: new[] { "TenantId", "Status" },
                filter: "[Status] = 'PendingApproval'");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_TenantId_Status_RequestedAtUtc",
                table: "ApprovalRequests",
                columns: new[] { "TenantId", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalRequests_TenantId_DocumentType_DocumentId",
                table: "ApprovalRequests",
                columns: new[] { "TenantId", "DocumentType", "DocumentId" },
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropIndex(
                name: "IX_Bills_TenantId_Status",
                table: "Bills");
        }
    }
}
