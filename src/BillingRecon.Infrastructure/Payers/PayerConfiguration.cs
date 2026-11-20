using BillingRecon.Domain.Payers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// The <c>Payers</c> table (docs/database.md, section 3.2). The id, the tenant column with its filter, guard and
/// foreign key, the audit columns, the row version, the kind and the status as text with their CHECK constraints come
/// from the conventions of the context; only what is particular to this table is configured here. Entity Framework
/// creates the instance, which is why the class is public.
/// </summary>
public sealed class PayerConfiguration : IEntityTypeConfiguration<Payer>
{
    private const int KindMaxLength = 20;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Payer> builder)
    {
        builder.ToTable("Payers");
        builder.HasKey(payer => payer.Id);

        builder.Property(payer => payer.Kind).HasMaxLength(KindMaxLength);

        builder.Property(payer => payer.Name).HasMaxLength(Payer.NameMaxLength);
        builder.HasIndex(payer => new { payer.TenantId, payer.Name }).HasDatabaseName("IX_Payers_TenantId_Name");

        // The patient the payer is. At most one payer per patient (BR-104); a guarantor has no patient, and the filter
        // leaves guarantors out of the uniqueness. The index also serves the foreign key, tenant first.
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(payer => payer.PatientId)
            .HasConstraintName("FK_Payers_PatientId");
        builder.HasIndex(payer => new { payer.TenantId, payer.PatientId })
            .IsUnique()
            .HasFilter("[PatientId] IS NOT NULL")
            .HasDatabaseName("UX_Payers_TenantId_PatientId");

        builder.Property(payer => payer.Phone).HasMaxLength(Payer.PhoneMaxLength);
        builder.Property(payer => payer.Email).HasMaxLength(Payer.EmailMaxLength);
        builder.Property(payer => payer.Status).HasMaxLength(StatusMaxLength);
    }
}
