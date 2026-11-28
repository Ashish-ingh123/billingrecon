using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingRecon.Infrastructure.Common.Migrations
{
    /// <inheritdoc />
    public partial class _0035_AccountantRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRoles_RoleCode",
                table: "UserRoles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Roles_Code",
                table: "Roles");

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Code", "Name" },
                values: new object[] { "Accountant", "Accountant" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserRoles_RoleCode",
                table: "UserRoles",
                sql: "[RoleCode] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer', 'Accountant')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Roles_Code",
                table: "Roles",
                sql: "[Code] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer', 'Accountant')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRoles_RoleCode",
                table: "UserRoles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Roles_Code",
                table: "Roles");

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Code",
                keyValue: "Accountant");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserRoles_RoleCode",
                table: "UserRoles",
                sql: "[RoleCode] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Roles_Code",
                table: "Roles",
                sql: "[Code] IN ('TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer')");
        }
    }
}
