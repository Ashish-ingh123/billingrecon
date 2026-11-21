using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The <c>UserRoles</c> table (docs/database.md section 3.1): one row per role a user holds, keyed by the user and the
/// role, the role stored as its code and a foreign key to <c>Roles</c>.
/// </summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(role => new { role.UserId, role.Role });
        builder.Property(role => role.Role).HasColumnName("RoleCode").HasMaxLength(RoleDefinition.CodeMaxLength);

        builder.HasOne<RoleDefinition>()
            .WithMany()
            .HasForeignKey(role => role.Role)
            .HasPrincipalKey(definition => definition.Code)
            .HasConstraintName("FK_UserRoles_RoleCode");

        // The holders of a role in the tenant: who the tenant's administrators are (ADR 0030).
        builder.HasIndex(role => new { role.TenantId, role.Role }).HasDatabaseName("IX_UserRoles_TenantId_RoleCode");
    }
}
