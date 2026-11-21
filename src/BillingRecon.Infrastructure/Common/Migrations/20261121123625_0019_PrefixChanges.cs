using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0019_PrefixChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.CreateTable(
                name: "PrefixChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FromPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    ToPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("PK_PrefixChanges", x => x.Id);
                    table.CheckConstraint("CK_PrefixChanges_ChangesThePrefix", "[FromPrefix] <> [ToPrefix] COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_PrefixChanges_Decided", "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'Pending' AND [DecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_PrefixChanges_DocumentType", "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund')");
                    table.CheckConstraint("CK_PrefixChanges_FromPrefix", "LEN([FromPrefix]) BETWEEN 1 AND 3 AND [FromPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_PrefixChanges_Status", "[Status] IN ('Pending', 'Approved', 'Rejected')");
                    table.CheckConstraint("CK_PrefixChanges_ToPrefix", "LEN([ToPrefix]) BETWEEN 1 AND 3 AND [ToPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.ForeignKey(
                        name: "FK_PrefixChanges_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrefixChanges_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrefixChanges_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_PostingApprovalThreshold",
                table: "Tenants",
                sql: "[PostingApprovalThreshold] IS NULL OR [PostingApprovalThreshold] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_PrefixesDistinct",
                table: "Tenants",
                sql: "[BillPrefix] NOT IN ([CreditNotePrefix], [PaymentPrefix], [AdjustmentPrefix], [RefundPrefix]) AND [CreditNotePrefix] NOT IN ([PaymentPrefix], [AdjustmentPrefix], [RefundPrefix]) AND [PaymentPrefix] NOT IN ([AdjustmentPrefix], [RefundPrefix]) AND [AdjustmentPrefix] NOT IN ([RefundPrefix])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'PrefixChange')");

            migrationBuilder.CreateIndex(
                name: "IX_PrefixChanges_TenantId_CreatedAtUtc",
                table: "PrefixChanges",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_PrefixChanges_TenantId_DocumentType_Pending",
                table: "PrefixChanges",
                columns: new[] { "TenantId", "DocumentType" },
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrefixChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_PostingApprovalThreshold",
                table: "Tenants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_PrefixesDistinct",
                table: "Tenants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_DocumentType",
                table: "ApprovalRequests",
                sql: "[DocumentType] IN ('Bill', 'CreditNote', 'Adjustment', 'Refund', 'Payment', 'PaymentAllocation', 'CreditNoteAllocation', 'NumberSeries')");
        }
    }
}
