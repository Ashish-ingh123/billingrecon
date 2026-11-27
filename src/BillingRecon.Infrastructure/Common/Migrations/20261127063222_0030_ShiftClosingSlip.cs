using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0030_ShiftClosingSlip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrintSettings_DocumentType",
                table: "PrintSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentPrints_DocumentType",
                table: "DocumentPrints");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrintSettings_DocumentType",
                table: "PrintSettings",
                sql: "[DocumentType] IN ('Bill', 'Payment', 'Refund', 'ShiftClosing')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentPrints_DocumentType",
                table: "DocumentPrints",
                sql: "[DocumentType] IN ('Bill', 'Payment', 'Refund', 'ShiftClosing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrintSettings_DocumentType",
                table: "PrintSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentPrints_DocumentType",
                table: "DocumentPrints");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrintSettings_DocumentType",
                table: "PrintSettings",
                sql: "[DocumentType] IN ('Bill', 'Payment', 'Refund')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentPrints_DocumentType",
                table: "DocumentPrints",
                sql: "[DocumentType] IN ('Bill', 'Payment', 'Refund')");
        }
    }
}
