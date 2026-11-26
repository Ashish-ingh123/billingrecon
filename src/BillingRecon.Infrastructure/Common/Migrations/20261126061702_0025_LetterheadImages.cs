using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0025_LetterheadImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoSha256",
                table: "Tenants",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatoryDesignation",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureSha256",
                table: "Tenants",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    StoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantImages", x => x.Id);
                    table.CheckConstraint("CK_TenantImages_Content", "DATALENGTH([Content]) BETWEEN 1 AND 262144");
                    table.CheckConstraint("CK_TenantImages_Format", "[Format] IN ('Png', 'Jpeg', 'Webp')");
                    table.CheckConstraint("CK_TenantImages_Height", "[Height] BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_TenantImages_Kind", "[Kind] IN ('Logo', 'Signature')");
                    table.CheckConstraint("CK_TenantImages_Width", "[Width] BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_TenantImages_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_TenantImages_TenantId_Kind_Sha256",
                table: "TenantImages",
                columns: new[] { "TenantId", "Kind", "Sha256" },
                unique: true);

            // Written by hand, as ADR 0034 asks of the table of an append-only entity: the application's role stores an
            // image and never changes or deletes one (ADR 0059). The role exists since 0020_DatabaseRoles.
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[TenantImages] TO [billingrecon_application];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantImages");

            migrationBuilder.DropColumn(
                name: "LogoSha256",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignatoryDesignation",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignatureSha256",
                table: "Tenants");
        }
    }
}
