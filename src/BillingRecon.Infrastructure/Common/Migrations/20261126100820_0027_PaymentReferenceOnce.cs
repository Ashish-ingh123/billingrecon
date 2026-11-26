using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0027_PaymentReferenceOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_PayerId_Method_Reference",
                table: "Payments",
                columns: new[] { "TenantId", "PayerId", "Method", "Reference" },
                unique: true,
                filter: "[Reference] IS NOT NULL AND [Method] <> 'Cash' AND [Status] <> 'Reversed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Payments_TenantId_PayerId_Method_Reference",
                table: "Payments");
        }
    }
}
