using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0034_ListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_RefundDate",
                table: "Refunds",
                columns: new[] { "TenantId", "RefundDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_UnappliedAmount",
                table: "Payments",
                columns: new[] { "TenantId", "UnappliedAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_CreditNoteDate",
                table: "CreditNotes",
                columns: new[] { "TenantId", "CreditNoteDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_BalanceDue",
                table: "Bills",
                columns: new[] { "TenantId", "BalanceDue" });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_TenantId_AdjustmentDate",
                table: "Adjustments",
                columns: new[] { "TenantId", "AdjustmentDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refunds_TenantId_RefundDate",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_UnappliedAmount",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_TenantId_CreditNoteDate",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_Bills_TenantId_BalanceDue",
                table: "Bills");

            migrationBuilder.DropIndex(
                name: "IX_Adjustments_TenantId_AdjustmentDate",
                table: "Adjustments");
        }
    }
}
