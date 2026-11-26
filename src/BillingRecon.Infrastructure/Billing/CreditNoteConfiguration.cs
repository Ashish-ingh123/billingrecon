using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>CreditNotes</c> table (docs/database.md, section 3.3; BR-210 to BR-214). The id, the tenant column with its
/// filter, guard and foreign key, the audit columns, the row version, the date, the money columns and the status as
/// text with its CHECK constraint come from the conventions of the context. The foreign keys to the bill, the payer
/// and the patient come from the convention for typed-id columns (ADR 0021), so this class names no entity of the
/// Payers module.
/// </summary>
public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes", table =>
        {
            table.HasCheckConstraint("CK_CreditNotes_SubTotal", "[SubTotal] >= 0");
            table.HasCheckConstraint("CK_CreditNotes_DiscountTotal", "[DiscountTotal] >= 0");
            table.HasCheckConstraint("CK_CreditNotes_TaxTotal", "[TaxTotal] >= 0");
            table.HasCheckConstraint("CK_CreditNotes_Total", "[Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
            table.HasCheckConstraint("CK_CreditNotes_AvailableAmount", "[AvailableAmount] BETWEEN 0 AND [Total]");
            table.HasCheckConstraint(
                "CK_CreditNotes_VoidedIffVoided",
                "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
            table.HasCheckConstraint(
                "CK_CreditNotes_NumberIffPosted",
                "([Status] <> 'Posted' AND [Number] IS NULL AND [PostedAtUtc] IS NULL) OR ([Status] = 'Posted' AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [Total] > 0)");
            table.HasCheckConstraint(
                "CK_CreditNotes_NumberParts",
                "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
        });
        builder.HasKey(creditNote => creditNote.Id);

        // The credit notes of a bill by status with their totals: the cap of BR-210 without reading the rows.
        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.BillId, creditNote.Status })
            .IncludeProperties(creditNote => new { creditNote.Total })
            .HasDatabaseName("IX_CreditNotes_TenantId_BillId_Status");

        // The credit a payer has available, for a statement.
        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.PayerId, creditNote.Status })
            .IncludeProperties(creditNote => new { creditNote.AvailableAmount })
            .HasDatabaseName("IX_CreditNotes_TenantId_PayerId_Status");

        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.PatientId }).HasDatabaseName("IX_CreditNotes_TenantId_PatientId");

        // The search of the list by an exact amount (Prompt 11.1; ADR 0036, amended 2026-11-26).
        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.Total }).HasDatabaseName("IX_CreditNotes_TenantId_Total");

        // The gapless number of a posted credit note (BR-801): unique in the tenant, and one credit note per position
        // of the series of a fiscal year. Drafts have neither, so both indexes leave them out.
        builder.Property(creditNote => creditNote.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.Number })
            .IsUnique()
            .HasFilter("[Number] IS NOT NULL")
            .HasDatabaseName("UX_CreditNotes_TenantId_Number");
        builder.HasIndex(creditNote => new { creditNote.TenantId, creditNote.FiscalYear, creditNote.SequenceNumber })
            .IsUnique()
            .HasFilter("[SequenceNumber] IS NOT NULL")
            .HasDatabaseName("UX_CreditNotes_TenantId_FiscalYear_SequenceNumber");

        builder.Property(creditNote => creditNote.Reason).HasMaxLength(CreditNote.ReasonMaxLength);
        builder.Property(creditNote => creditNote.Status).HasMaxLength(StatusMaxLength);
        builder.Property(creditNote => creditNote.VoidReason).HasMaxLength(CreditNote.ReasonMaxLength);

        // The lines belong to the credit note and are loaded, changed and saved with it. A line taken out of a draft
        // is deleted by Entity Framework (ClientCascade); the database itself deletes nothing.
        builder.HasMany(creditNote => creditNote.Lines)
            .WithOne()
            .HasForeignKey(line => line.CreditNoteId)
            .HasConstraintName("FK_CreditNoteLines_CreditNoteId")
            .OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(creditNote => creditNote.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The applications of the credit are never removed.
        builder.HasMany(creditNote => creditNote.Allocations)
            .WithOne()
            .HasForeignKey(allocation => allocation.CreditNoteId)
            .HasConstraintName("FK_CreditNoteAllocations_CreditNoteId")
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(creditNote => creditNote.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
