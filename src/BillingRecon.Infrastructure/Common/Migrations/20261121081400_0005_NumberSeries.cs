using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0005_NumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumberSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FiscalYear = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeries", x => x.Id);
                    table.CheckConstraint("CK_NumberSeries_DocumentType", "[DocumentType] IN ('Bill', 'CreditNote', 'Payment', 'Adjustment', 'Refund')");
                    table.CheckConstraint("CK_NumberSeries_NextNumber", "[NextNumber] >= 1");
                    table.CheckConstraint("CK_NumberSeries_Prefix", "LEN([Prefix]) BETWEEN 1 AND 3 AND [Prefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.ForeignKey(
                        name: "FK_NumberSeries_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_NumberSeries_TenantId_DocumentType_FiscalYear",
                table: "NumberSeries",
                columns: new[] { "TenantId", "DocumentType", "FiscalYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumberSeries");
        }
    }
}
