using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0001_Tenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PostingApprovalThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxBackdateDays = table.Column<int>(type: "int", nullable: false),
                    DefaultDueDays = table.Column<int>(type: "int", nullable: false),
                    BillPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    CreditNotePrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    PaymentPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    AdjustmentPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    RefundPrefix = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_AdjustmentPrefix", "LEN([AdjustmentPrefix]) BETWEEN 1 AND 3 AND [AdjustmentPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_Tenants_BillPrefix", "LEN([BillPrefix]) BETWEEN 1 AND 3 AND [BillPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_Tenants_CreditNotePrefix", "LEN([CreditNotePrefix]) BETWEEN 1 AND 3 AND [CreditNotePrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_Tenants_DefaultDueDays", "[DefaultDueDays] BETWEEN 0 AND 365");
                    table.CheckConstraint("CK_Tenants_MaxBackdateDays", "[MaxBackdateDays] BETWEEN 0 AND 365");
                    table.CheckConstraint("CK_Tenants_PaymentPrefix", "LEN([PaymentPrefix]) BETWEEN 1 AND 3 AND [PaymentPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_Tenants_RefundPrefix", "LEN([RefundPrefix]) BETWEEN 1 AND 3 AND [RefundPrefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_Tenants_Status", "[Status] IN ('Active', 'Suspended')");
                });

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
