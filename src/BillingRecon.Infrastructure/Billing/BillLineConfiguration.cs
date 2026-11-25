using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>BillLines</c> table (docs/database.md, section 3.3): owned by <c>Bills</c>, with no row version of its own.
/// The id, the tenant column with its filter, guard and foreign key and the sizes of the quantity, the unit price, the
/// tax rate and the money columns come from the conventions of the context. The relationship to the bill is declared
/// in <see cref="BillConfiguration"/>.
/// </summary>
public sealed class BillLineConfiguration : IEntityTypeConfiguration<BillLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BillLine> builder)
    {
        // The value objects and the aggregate already refuse all of this; the constraints are the last line of
        // defence. The two products of BR-202 are not checked, only the sums (docs/database.md, section 3.3).
        builder.ToTable("BillLines", table =>
        {
            table.HasCheckConstraint("CK_BillLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_BillLines_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_BillLines_UnitPrice", "[UnitPrice] >= 0");
            table.HasCheckConstraint("CK_BillLines_LineAmount", "[LineAmount] >= 0");
            table.HasCheckConstraint("CK_BillLines_DiscountAmount", "[DiscountAmount] BETWEEN 0 AND [LineAmount]");
            table.HasCheckConstraint("CK_BillLines_TaxRatePercent", "[TaxRatePercent] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_BillLines_TaxAmount", "[TaxAmount] >= 0");
            table.HasCheckConstraint("CK_BillLines_LineTotal", "[LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount]");
        });
        builder.HasKey(line => line.Id);

        // One line per position of a bill. The index leads with the tenant, like every index of a tenant-scoped
        // table, and also serves the foreign key to the bill.
        builder.HasIndex(line => new { line.TenantId, line.BillId, line.LineNo })
            .IsUnique()
            .HasDatabaseName("UX_BillLines_TenantId_BillId_LineNo");

        // The catalogue entry the line was priced from. Same module, so the entity may be named.
        builder.HasOne<ServiceItem>()
            .WithMany()
            .HasForeignKey(line => line.ServiceItemId)
            .HasConstraintName("FK_BillLines_ServiceItemId");

        builder.Property(line => line.Description).HasMaxLength(BillLine.DescriptionMaxLength);

        // The SAC code the catalogue entry had when the line was written (ADR 0054).
        builder.Property(line => line.SacCode).HasMaxLength(ServiceItem.SacCodeLength).IsFixedLength().IsUnicode(false);
        builder.Property(line => line.TaxRate).HasColumnName("TaxRatePercent");
    }
}
