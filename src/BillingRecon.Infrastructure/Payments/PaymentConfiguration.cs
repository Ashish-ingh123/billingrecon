using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The <c>Payments</c> table as recording needs it (docs/database.md, section 3.4; BR-301, BR-302, BR-405). A payment
/// is posted when it is created, so its number is never null. The foreign keys to the payer and to the ledger posting
/// come from the convention of the context for typed-id columns (ADR 0021), so this class names no entity of another
/// module. The columns and states of a reversal arrive with reversals (Prompt 4.5).
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private const int MethodMaxLength = 20;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_SequenceNumber", "[SequenceNumber] >= 1");
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Payments_UnappliedAmount", "[UnappliedAmount] BETWEEN 0 AND [Amount]");
            table.HasCheckConstraint("CK_Payments_Reference", "[Method] = 'Cash' OR [Reference] IS NOT NULL");
        });
        builder.HasKey(payment => payment.Id);

        // The gapless number (BR-801): unique in the tenant, and one payment per position of the series of a fiscal year.
        builder.Property(payment => payment.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.HasIndex(payment => new { payment.TenantId, payment.Number })
            .IsUnique()
            .HasDatabaseName("UX_Payments_TenantId_Number");
        builder.HasIndex(payment => new { payment.TenantId, payment.FiscalYear, payment.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("UX_Payments_TenantId_FiscalYear_SequenceNumber");

        // The payments of a payer with what is left of each, without reading the rows.
        builder.HasIndex(payment => new { payment.TenantId, payment.PayerId, payment.Status })
            .IncludeProperties(payment => new { payment.UnappliedAmount })
            .HasDatabaseName("IX_Payments_TenantId_PayerId_Status");

        // The collection of a day by method (BR-901), without reading the rows.
        builder.HasIndex(payment => new { payment.TenantId, payment.PaymentDate })
            .IncludeProperties(payment => new { payment.Method, payment.Amount, payment.Status })
            .HasDatabaseName("IX_Payments_TenantId_PaymentDate");

        builder.Property(payment => payment.Method).HasMaxLength(MethodMaxLength);
        builder.Property(payment => payment.Status).HasMaxLength(StatusMaxLength);
        builder.Property(payment => payment.Reference).HasMaxLength(Payment.ReferenceMaxLength);
        builder.Property(payment => payment.Notes).HasMaxLength(Payment.NotesMaxLength);
    }
}
