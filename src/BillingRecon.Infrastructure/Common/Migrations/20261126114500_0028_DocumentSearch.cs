using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0028_DocumentSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_Amount",
                table: "Refunds",
                columns: new[] { "TenantId", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Amount",
                table: "Payments",
                columns: new[] { "TenantId", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_Total",
                table: "CreditNotes",
                columns: new[] { "TenantId", "Total" });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_TenantId_Total",
                table: "Bills",
                columns: new[] { "TenantId", "Total" });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_TenantId_Amount",
                table: "Adjustments",
                columns: new[] { "TenantId", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refunds_TenantId_Amount",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_Amount",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_TenantId_Total",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_Bills_TenantId_Total",
                table: "Bills");

            migrationBuilder.DropIndex(
                name: "IX_Adjustments_TenantId_Amount",
                table: "Adjustments");
        }
    }
}
