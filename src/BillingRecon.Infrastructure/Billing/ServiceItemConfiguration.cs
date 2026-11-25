using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>ServiceItems</c> table (docs/database.md, section 3.3). The id, the tenant column with its filter, guard and
/// foreign key, the audit columns, the row version, the precision of the price and of the tax rate, and the status as
/// text with its CHECK constraint come from the conventions of the context; only what is particular to this table is
/// configured here. Entity Framework creates the instance, which is why the class is public.
/// </summary>
public sealed class ServiceItemConfiguration : IEntityTypeConfiguration<ServiceItem>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceItem> builder)
    {
        // The value objects already refuse a negative price and a tax rate outside 0 to 100; the constraints are the
        // last line of defence for a row written by anything else.
        builder.ToTable("ServiceItems", table =>
        {
            table.HasCheckConstraint("CK_ServiceItems_DefaultUnitPrice", "[DefaultUnitPrice] >= 0");
            table.HasCheckConstraint("CK_ServiceItems_TaxRatePercent", "[TaxRatePercent] BETWEEN 0 AND 100");
        });
        builder.HasKey(item => item.Id);

        // The code is stored as entered, in ASCII. Under the case-insensitive collation of the database the unique
        // index treats codes that differ only in case as the same, as ServiceItemCode itself does.
        builder.Property(item => item.Code)
            .HasConversion(code => code.Value, value => ServiceItemCode.Of(value))
            .HasMaxLength(ServiceItemCode.MaxLength)
            .IsUnicode(false);
        builder.HasIndex(item => new { item.TenantId, item.Code })
            .IsUnique()
            .HasDatabaseName("UX_ServiceItems_TenantId_Code");

        builder.Property(item => item.Name).HasMaxLength(ServiceItem.NameMaxLength);
        builder.HasIndex(item => new { item.TenantId, item.Name }).HasDatabaseName("IX_ServiceItems_TenantId_Name");

        builder.Property(item => item.Category).HasMaxLength(ServiceItem.CategoryMaxLength);

        // The services accounting code, six digits, when the hospital gave one (ADR 0054).
        builder.Property(item => item.SacCode).HasMaxLength(ServiceItem.SacCodeLength).IsFixedLength().IsUnicode(false);
        builder.Property(item => item.TaxRate).HasColumnName("TaxRatePercent");
        builder.Property(item => item.Status).HasMaxLength(StatusMaxLength);
    }
}
