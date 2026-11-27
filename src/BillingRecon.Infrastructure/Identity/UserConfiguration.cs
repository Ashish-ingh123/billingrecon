using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The <c>Users</c> table (docs/database.md section 3.1; migration <c>0018_Users</c>). With it in the model, every
/// column of the model that holds a user's id becomes a foreign key to it, by the typed-id convention (ADR 0021,
/// ADR 0030).
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
        {
            table.HasCheckConstraint("CK_Users_SecondStepFailures", "[SecondStepFailures] >= 0");
            table.HasCheckConstraint("CK_Users_PasswordResetRequests", "[PasswordResetRequests] >= 0");
        });
        builder.HasKey(user => user.Id);

        // The address is unique in the tenant without regard to case (BR-601): the index compares the upper-cased form.
        builder.Property(user => user.Email).HasMaxLength(User.EmailMaxLength);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(User.EmailMaxLength);
        builder.HasIndex(user => new { user.TenantId, user.NormalizedEmail }).IsUnique().HasDatabaseName("UX_Users_TenantId_NormalizedEmail");

        builder.Property(user => user.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        builder.Property(user => user.Status).HasMaxLength(StatusMaxLength);

        // The hash is the one column no audit image may hold (ADR 0030).
        builder.Property(user => user.PasswordHash).HasMaxLength(User.PasswordHashMaxLength).IsUnicode(false).HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);

        // The second step (ADR 0061). Its secrets, protected as they are, and the hashes of the recovery codes are in
        // no audit image, like the hash of the password.
        builder.Property(user => user.SecondStepSecret).HasMaxLength(User.ProtectedSecretMaxLength).IsUnicode(false).HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);
        builder.Property(user => user.SecondStepPendingSecret).HasMaxLength(User.ProtectedSecretMaxLength).IsUnicode(false).HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);
        builder.Property(user => user.RecoveryCodeHashes).HasMaxLength(User.RecoveryCodeHashesMaxLength).IsUnicode(false).HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);
        builder.Ignore(user => user.SecondStepEnrolled);

        // The link of a forgotten password (ADR 0061, amended): only its hash, and that in no audit image either.
        builder.Property(user => user.PasswordResetTokenHash).HasMaxLength(User.PasswordResetTokenHashLength).IsUnicode(false).HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);

        // The roles are rows of the user, deleted when the user no longer holds them.
        builder.HasMany(user => user.Roles)
            .WithOne()
            .HasForeignKey(role => role.UserId)
            .HasConstraintName("FK_UserRoles_UserId")
            .OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(user => user.IsActiveTenantAdmin);
    }
}
