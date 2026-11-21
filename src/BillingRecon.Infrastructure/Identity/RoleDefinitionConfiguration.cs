using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The global <c>Roles</c> table (docs/database.md section 3.1): the five roles, written by the migration from the
/// model (<see cref="RoleDefinition.All"/>), not by a hand-written statement. Not tenant-scoped: every tenant has the
/// same roles. The tests reset every table but this one.
/// </summary>
public sealed class RoleDefinitionConfiguration : IEntityTypeConfiguration<RoleDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoleDefinition> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(definition => definition.Code);
        builder.Property(definition => definition.Code).HasMaxLength(RoleDefinition.CodeMaxLength);
        builder.Property(definition => definition.Name).HasMaxLength(RoleDefinition.NameMaxLength);
        builder.HasData(RoleDefinition.All);
    }
}
