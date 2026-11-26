using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>
/// The <c>Adjustments</c> table (docs/database.md, section 3.5; BR-501, BR-502). The id, the tenant column with its
/// filter, guard and foreign key, the audit columns, the row version, the date, the money column and the kind and the
/// status as text with their CHECK constraints come from the conventions of the context. The foreign keys to the
/// bill, the payer and the ledger posting come from the convention for typed-id columns (ADR 0021), so this class
/// names no entity of another module.
/// </summary>
public sealed class AdjustmentConfiguration : IEntityTypeConfiguration<Adjustment>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Adjustment> builder)
    {
        builder.ToTable("Adjustments", table =>
        {
            // More than nothing, and a rounding is less than one rupee (BR-501).
            table.HasCheckConstraint("CK_Adjustments_Amount", "[Amount] > 0 AND ([Kind] <> 'Rounding' OR [Amount] < 1.00)");
            table.HasCheckConstraint(
                "CK_Adjustments_VoidedIffVoided",
                "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
            table.HasCheckConstraint(
                "CK_Adjustments_NumberIffPosted",
                "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL AND [PostingId] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [PostingId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Adjustments_NumberParts",
                "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
        });
        builder.HasKey(adjustment => adjustment.Id);

        // The adjustments of a bill by status with their amounts, without reading the rows.
        builder.HasIndex(adjustment => new { adjustment.TenantId, adjustment.BillId, adjustment.Status })
            .IncludeProperties(adjustment => new { adjustment.Amount })
            .HasDatabaseName("IX_Adjustments_TenantId_BillId_Status");

        builder.HasIndex(adjustment => new { adjustment.TenantId, adjustment.PayerId }).HasDatabaseName("IX_Adjustments_TenantId_PayerId");

        // The search of the list by an exact amount (Prompt 11.1; ADR 0036, amended 2026-11-26).
        builder.HasIndex(adjustment => new { adjustment.TenantId, adjustment.Amount }).HasDatabaseName("IX_Adjustments_TenantId_Amount");

        // The gapless number of a posted adjustment (BR-801): unique in the tenant, and one adjustment per position of
        // the series of a fiscal year. Drafts have neither, so both indexes leave them out.
        builder.Property(adjustment => adjustment.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.HasIndex(adjustment => new { adjustment.TenantId, adjustment.Number })
            .IsUnique()
            .HasFilter("[Number] IS NOT NULL")
            .HasDatabaseName("UX_Adjustments_TenantId_Number");
        builder.HasIndex(adjustment => new { adjustment.TenantId, adjustment.FiscalYear, adjustment.SequenceNumber })
            .IsUnique()
            .HasFilter("[SequenceNumber] IS NOT NULL")
            .HasDatabaseName("UX_Adjustments_TenantId_FiscalYear_SequenceNumber");

        builder.Property(adjustment => adjustment.Kind).HasMaxLength(EnumMaxLength);
        builder.Property(adjustment => adjustment.Status).HasMaxLength(EnumMaxLength);
        builder.Property(adjustment => adjustment.Reason).HasMaxLength(Adjustment.ReasonMaxLength);
        builder.Property(adjustment => adjustment.VoidReason).HasMaxLength(Adjustment.ReasonMaxLength);
    }
}
