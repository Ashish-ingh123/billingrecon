using BillingRecon.Domain.Payers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>
/// The <c>Patients</c> table (docs/database.md, section 3.2). The id, the tenant column with its filter, guard and
/// foreign key, the audit columns, the row version, the date of birth as <c>date</c>, the sex and the status as text
/// with their CHECK constraints come from the conventions of the context; only what is particular to this table is
/// configured here. Entity Framework creates the instance, which is why the class is public.
/// </summary>
public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    private const int SexMaxLength = 10;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(patient => patient.Id);

        // The MRN is stored as entered. Under the case-insensitive collation of the database the unique index treats
        // MRNs that differ only in case as the same, as Mrn itself does (BR-101).
        builder.Property(patient => patient.Mrn)
            .HasConversion(mrn => mrn.Value, value => Mrn.Of(value))
            .HasMaxLength(Mrn.MaxLength);
        builder.HasIndex(patient => new { patient.TenantId, patient.Mrn })
            .IsUnique()
            .HasDatabaseName("UX_Patients_TenantId_Mrn");

        builder.Property(patient => patient.FullName).HasMaxLength(Patient.FullNameMaxLength);
        builder.HasIndex(patient => new { patient.TenantId, patient.FullName }).HasDatabaseName("IX_Patients_TenantId_FullName");

        builder.Property(patient => patient.Sex).HasMaxLength(SexMaxLength);
        builder.Property(patient => patient.Phone).HasMaxLength(Patient.PhoneMaxLength);
        builder.Property(patient => patient.Email).HasMaxLength(Patient.EmailMaxLength);
        builder.Property(patient => patient.AddressLine1).HasMaxLength(Patient.AddressLineMaxLength);
        builder.Property(patient => patient.AddressLine2).HasMaxLength(Patient.AddressLineMaxLength);
        builder.Property(patient => patient.City).HasMaxLength(Patient.CityMaxLength);
        builder.Property(patient => patient.State).HasMaxLength(Patient.StateMaxLength);
        builder.Property(patient => patient.PinCode).HasMaxLength(Patient.PinCodeMaxLength).IsUnicode(false);
        builder.Property(patient => patient.Status).HasMaxLength(StatusMaxLength);
    }
}
