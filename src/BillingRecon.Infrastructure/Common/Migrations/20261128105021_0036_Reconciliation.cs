using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0036_Reconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SettlementChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountLabel = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: true),
                    Gateway = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    BankChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateWindowDays = table.Column<int>(type: "int", nullable: false),
                    Mapping = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementChannels", x => x.Id);
                    table.CheckConstraint("CK_SettlementChannels_DateWindowDays", "[DateWindowDays] BETWEEN 0 AND 10");
                    table.CheckConstraint("CK_SettlementChannels_Gateway", "[Gateway] IN ('Razorpay')");
                    table.CheckConstraint("CK_SettlementChannels_Kind", "[Kind] IN ('Bank', 'Gateway')");
                    table.CheckConstraint("CK_SettlementChannels_Status", "[Status] IN ('Active', 'Inactive')");
                    table.ForeignKey(
                        name: "FK_SettlementChannels_BankChannelId",
                        column: x => x.BankChannelId,
                        principalTable: "SettlementChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementChannels_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementChannels_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementChannels_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatementImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RowsRead = table.Column<int>(type: "int", nullable: false),
                    LinesAdded = table.Column<int>(type: "int", nullable: false),
                    LinesAlreadyHeld = table.Column<int>(type: "int", nullable: false),
                    RowsRefused = table.Column<int>(type: "int", nullable: false),
                    FirstDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementImports", x => x.Id);
                    table.CheckConstraint("CK_StatementImports_Counts", "[RowsRead] = [LinesAdded] + [LinesAlreadyHeld] + [RowsRefused] AND [LinesAdded] >= 0 AND [LinesAlreadyHeld] >= 0 AND [RowsRefused] >= 0");
                    table.ForeignKey(
                        name: "FK_StatementImports_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "SettlementChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementImports_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementImports_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Direction = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RawText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Fingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Occurrence = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementLines", x => x.Id);
                    table.CheckConstraint("CK_StatementLines_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_StatementLines_Direction", "[Direction] IN ('In', 'Out')");
                    table.CheckConstraint("CK_StatementLines_RowNumber", "[RowNumber] >= 1 AND [Occurrence] >= 1");
                    table.CheckConstraint("CK_StatementLines_Status", "[Status] IN ('Unmatched', 'Matched', 'Excluded')");
                    table.ForeignKey(
                        name: "FK_StatementLines_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "SettlementChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementLines_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementLines_ImportId",
                        column: x => x.ImportId,
                        principalTable: "StatementImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementLines_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementLines_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementChannels_TenantId_BankChannelId",
                table: "SettlementChannels",
                columns: new[] { "TenantId", "BankChannelId" });

            migrationBuilder.CreateIndex(
                name: "UX_SettlementChannels_TenantId_Name",
                table: "SettlementChannels",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementImports_TenantId_CreatedAtUtc",
                table: "StatementImports",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StatementImports_TenantId_ChannelId_FileHash",
                table: "StatementImports",
                columns: new[] { "TenantId", "ChannelId", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_TenantId_ChannelId_Status_TransactionDate",
                table: "StatementLines",
                columns: new[] { "TenantId", "ChannelId", "Status", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_TenantId_ImportId",
                table: "StatementLines",
                columns: new[] { "TenantId", "ImportId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatementLines_TenantId_TransactionDate",
                table: "StatementLines",
                columns: new[] { "TenantId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "UX_StatementLines_TenantId_ChannelId_Fingerprint_Occurrence",
                table: "StatementLines",
                columns: new[] { "TenantId", "ChannelId", "Fingerprint", "Occurrence" },
                unique: true);

            // Written by hand (ADR 0034, as migrations 0024 and 0025 did): an import is a fact and never changes, so the
            // application's login may add one and read it, and neither change nor remove it.
            migrationBuilder.Sql("DENY UPDATE, DELETE ON [dbo].[StatementImports] TO [billingrecon_application];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatementLines");

            migrationBuilder.DropTable(
                name: "StatementImports");

            migrationBuilder.DropTable(
                name: "SettlementChannels");
        }
    }
}
