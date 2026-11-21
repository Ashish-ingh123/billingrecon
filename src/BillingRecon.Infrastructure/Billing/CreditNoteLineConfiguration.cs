using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>CreditNoteLines</c> table (docs/database.md, section 3.3): owned by <c>CreditNotes</c>, with no row version
/// of its own, shaped like <c>BillLines</c>. The foreign key to the line of the bill a line credits comes from the
/// convention for typed-id columns (ADR 0021). The relationship to the credit note is declared in
/// <see cref="CreditNoteConfiguration"/>.
/// </summary>
public sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        // The value objects and the aggregate already refuse all of this; the constraints are the last line of
        // defence. The two products of BR-202 are not checked, only the sums.
        builder.ToTable("CreditNoteLines", table =>
        {
            table.HasCheckConstraint("CK_CreditNoteLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_CreditNoteLines_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_CreditNoteLines_UnitPrice", "[UnitPrice] >= 0");
            table.HasCheckConstraint("CK_CreditNoteLines_LineAmount", "[LineAmount] >= 0");
            table.HasCheckConstraint("CK_CreditNoteLines_DiscountAmount", "[DiscountAmount] BETWEEN 0 AND [LineAmount]");
            table.HasCheckConstraint("CK_CreditNoteLines_TaxRatePercent", "[TaxRatePercent] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_CreditNoteLines_TaxAmount", "[TaxAmount] >= 0");
            table.HasCheckConstraint("CK_CreditNoteLines_LineTotal", "[LineTotal] = [LineAmount] - [DiscountAmount] + [TaxAmount]");
        });
        builder.HasKey(line => line.Id);

        // One line per position of a credit note; also serves the foreign key to the credit note.
        builder.HasIndex(line => new { line.TenantId, line.CreditNoteId, line.LineNo })
            .IsUnique()
            .HasDatabaseName("UX_CreditNoteLines_TenantId_CreditNoteId_LineNo");

        // What is credited for a line of a bill: the cap of BR-211 without reading the rows.
        builder.HasIndex(line => new { line.TenantId, line.BillLineId })
            .IncludeProperties(line => new { line.LineTotal })
            .HasDatabaseName("IX_CreditNoteLines_TenantId_BillLineId");

        builder.Property(line => line.Description).HasMaxLength(BillLine.DescriptionMaxLength);
        builder.Property(line => line.TaxRate).HasColumnName("TaxRatePercent");
    }
}
