using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0018_Users : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Code);
                    table.CheckConstraint("CK_Roles_Code", "[Code] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer')");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LastSignInAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Status", "[Status] IN ('Active', 'Disabled')");
                    table.ForeignKey(
                        name: "FK_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleCode });
                    table.CheckConstraint("CK_UserRoles_RoleCode", "[RoleCode] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer')");
                    table.ForeignKey(
                        name: "FK_UserRoles_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_RoleCode",
                        column: x => x.RoleCode,
                        principalTable: "Roles",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Code", "Name" },
                values: new object[,]
                {
                    { "BillingClerk", "Billing clerk" },
                    { "Cashier", "Cashier" },
                    { "Supervisor", "Supervisor" },
                    { "TenantAdmin", "Tenant administrator" },
                    { "Viewer", "Viewer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_TenantId_RoleCode",
                table: "UserRoles",
                columns: new[] { "TenantId", "RoleCode" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_TenantId_NormalizedEmail",
                table: "Users",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Adjustments_CreatedByUserId",
                table: "Adjustments",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Adjustments_PostedByUserId",
                table: "Adjustments",
                column: "PostedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Adjustments_UpdatedByUserId",
                table: "Adjustments",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Adjustments_VoidedByUserId",
                table: "Adjustments",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_CheckerUserId",
                table: "ApprovalRequests",
                column: "CheckerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_MakerUserId",
                table: "ApprovalRequests",
                column: "MakerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditRecords_UserId",
                table: "AuditRecords",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bills_CreatedByUserId",
                table: "Bills",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bills_PostedByUserId",
                table: "Bills",
                column: "PostedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bills_UpdatedByUserId",
                table: "Bills",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bills_VoidedByUserId",
                table: "Bills",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteAllocations_AllocatedByUserId",
                table: "CreditNoteAllocations",
                column: "AllocatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteAllocations_ReversedByUserId",
                table: "CreditNoteAllocations",
                column: "ReversedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_CreatedByUserId",
                table: "CreditNotes",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_PostedByUserId",
                table: "CreditNotes",
                column: "PostedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_UpdatedByUserId",
                table: "CreditNotes",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_VoidedByUserId",
                table: "CreditNotes",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyKeys_UserId",
                table: "IdempotencyKeys",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerPostings_PostedByUserId",
                table: "LedgerPostings",
                column: "PostedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_CreatedByUserId",
                table: "Patients",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_UpdatedByUserId",
                table: "Patients",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payers_CreatedByUserId",
                table: "Payers",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payers_UpdatedByUserId",
                table: "Payers",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_AllocatedByUserId",
                table: "PaymentAllocations",
                column: "AllocatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_ReversedByUserId",
                table: "PaymentAllocations",
                column: "ReversedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CreatedByUserId",
                table: "Payments",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_ReversedByUserId",
                table: "Payments",
                column: "ReversedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_UpdatedByUserId",
                table: "Payments",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_CreatedByUserId",
                table: "Refunds",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_PostedByUserId",
                table: "Refunds",
                column: "PostedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_UpdatedByUserId",
                table: "Refunds",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_VoidedByUserId",
                table: "Refunds",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceItems_CreatedByUserId",
                table: "ServiceItems",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceItems_UpdatedByUserId",
                table: "ServiceItems",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Adjustments_CreatedByUserId",
                table: "Adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_Adjustments_PostedByUserId",
                table: "Adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_Adjustments_UpdatedByUserId",
                table: "Adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_Adjustments_VoidedByUserId",
                table: "Adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequests_CheckerUserId",
                table: "ApprovalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequests_MakerUserId",
                table: "ApprovalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditRecords_UserId",
                table: "AuditRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Bills_CreatedByUserId",
                table: "Bills");

            migrationBuilder.DropForeignKey(
                name: "FK_Bills_PostedByUserId",
                table: "Bills");

            migrationBuilder.DropForeignKey(
                name: "FK_Bills_UpdatedByUserId",
                table: "Bills");

            migrationBuilder.DropForeignKey(
                name: "FK_Bills_VoidedByUserId",
                table: "Bills");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteAllocations_AllocatedByUserId",
                table: "CreditNoteAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteAllocations_ReversedByUserId",
                table: "CreditNoteAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_CreatedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_PostedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_UpdatedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_VoidedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyKeys_UserId",
                table: "IdempotencyKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerPostings_PostedByUserId",
                table: "LedgerPostings");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_CreatedByUserId",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_UpdatedByUserId",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Payers_CreatedByUserId",
                table: "Payers");

            migrationBuilder.DropForeignKey(
                name: "FK_Payers_UpdatedByUserId",
                table: "Payers");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_AllocatedByUserId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_ReversedByUserId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CreatedByUserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_ReversedByUserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_UpdatedByUserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_CreatedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_PostedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_UpdatedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_VoidedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceItems_CreatedByUserId",
                table: "ServiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceItems_UpdatedByUserId",
                table: "ServiceItems");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
