using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0024_PrintedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gstin",
                table: "Tenants",
                type: "char(15)",
                unicode: false,
                fixedLength: true,
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Tenants",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "Tenants",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Signatory",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SacCode",
                table: "ServiceItems",
                type: "char(6)",
                unicode: false,
                fixedLength: true,
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SacCode",
                table: "BillLines",
                type: "char(6)",
                unicode: false,
                fixedLength: true,
                maxLength: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentPrints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CopyNumber = table.Column<int>(type: "int", nullable: false),
                    PrintedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPrints", x => x.Id);
                    table.CheckConstraint("CK_DocumentPrints_CopyNumber", "[CopyNumber] >= 1");
                    table.CheckConstraint("CK_DocumentPrints_DocumentType", "[DocumentType] IN ('Bill', 'Payment', 'Refund')");
                    table.ForeignKey(
                        name: "FK_DocumentPrints_PrintedByUserId",
                        column: x => x.PrintedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentPrints_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentPrints_TenantId_DocumentType_DocumentId_CopyNumber",
                table: "DocumentPrints",
                columns: new[] { "TenantId", "DocumentType", "DocumentId", "CopyNumber" },
                unique: true);

            // Written by hand, as ADR 0034 asks of the table of an append-only entity: the application's role inserts a
            // print and never changes or deletes one. The role exists since 0020_DatabaseRoles.
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[DocumentPrints] TO [billingrecon_application];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPrints");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Gstin",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Signatory",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SacCode",
                table: "ServiceItems");

            migrationBuilder.DropColumn(
                name: "SacCode",
                table: "BillLines");
        }
    }
}
