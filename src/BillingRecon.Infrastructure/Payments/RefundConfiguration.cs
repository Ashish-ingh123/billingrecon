using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The <c>Refunds</c> table (docs/database.md, section 3.4; migration <c>0017_Refunds</c>). The foreign keys to payers,
/// payments, credit notes and ledger postings come from the typed-id convention (ADR 0021).
/// </summary>
public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds", table =>
        {
            table.HasCheckConstraint("CK_Refunds_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Refunds_Reference", "[Method] = 'Cash' OR [Reference] IS NOT NULL");

            // Exactly one source, the one the kind names (BR-307).
            table.HasCheckConstraint(
                "CK_Refunds_OneSource",
                "([SourceKind] = 'Payment' AND [SourcePaymentId] IS NOT NULL AND [SourceCreditNoteId] IS NULL) OR ([SourceKind] = 'CreditNote' AND [SourceCreditNoteId] IS NOT NULL AND [SourcePaymentId] IS NULL)");
            table.HasCheckConstraint(
                "CK_Refunds_VoidedIffVoided",
                "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
            table.HasCheckConstraint(
                "CK_Refunds_NumberIffPosted",
                "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL AND [PostingId] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [PostingId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Refunds_NumberParts",
                "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
        });
        builder.HasKey(refund => refund.Id);
        builder.Ignore(refund => refund.Source);

        builder.HasIndex(refund => new { refund.TenantId, refund.PayerId }).HasDatabaseName("IX_Refunds_TenantId_PayerId");

        // The search of the list by an exact amount (Prompt 11.1; ADR 0036, amended 2026-11-26).
        builder.HasIndex(refund => new { refund.TenantId, refund.Amount }).HasDatabaseName("IX_Refunds_TenantId_Amount");

        // The refunds of a payment or of a credit note by status with their amounts, without reading the rows: what the
        // reversal of a payment asks (BR-306), and what the reconciliation sums (identities 4 and 5).
        builder.HasIndex(refund => new { refund.TenantId, refund.SourcePaymentId })
            .IncludeProperties(refund => new { refund.Amount, refund.Status })
            .HasDatabaseName("IX_Refunds_TenantId_SourcePaymentId");
        builder.HasIndex(refund => new { refund.TenantId, refund.SourceCreditNoteId })
            .IncludeProperties(refund => new { refund.Amount, refund.Status })
            .HasDatabaseName("IX_Refunds_TenantId_SourceCreditNoteId");

        // The gapless number of a posted refund (BR-801): unique in the tenant, and one refund per position of the
        // series of a fiscal year. Drafts have neither, so both indexes leave them out.
        builder.Property(refund => refund.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.HasIndex(refund => new { refund.TenantId, refund.Number })
            .IsUnique()
            .HasFilter("[Number] IS NOT NULL")
            .HasDatabaseName("UX_Refunds_TenantId_Number");
        builder.HasIndex(refund => new { refund.TenantId, refund.FiscalYear, refund.SequenceNumber })
            .IsUnique()
            .HasFilter("[SequenceNumber] IS NOT NULL")
            .HasDatabaseName("UX_Refunds_TenantId_FiscalYear_SequenceNumber");

        builder.Property(refund => refund.SourceKind).HasMaxLength(EnumMaxLength);
        builder.Property(refund => refund.Method).HasMaxLength(EnumMaxLength);
        builder.Property(refund => refund.Status).HasMaxLength(EnumMaxLength);
        builder.Property(refund => refund.Reference).HasMaxLength(Refund.ReferenceMaxLength);
        builder.Property(refund => refund.Reason).HasMaxLength(Refund.ReasonMaxLength);
        builder.Property(refund => refund.VoidReason).HasMaxLength(Refund.ReasonMaxLength);
    }
}
