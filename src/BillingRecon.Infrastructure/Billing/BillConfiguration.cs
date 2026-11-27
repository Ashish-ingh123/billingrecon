using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Payers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>Bills</c> table as drafts need it (docs/database.md, section 3.3). The id, the tenant column with its filter,
/// guard and foreign key, the audit columns, the row version, the dates as <c>date</c>, the money columns as
/// <c>decimal(18,2)</c> and the status as text with its CHECK constraint come from the conventions of the context.
/// The number, the fiscal year, the posted stamps and their constraints arrive with the posting slice.
/// <para>
/// The patient and the payer belong to the Payers module. Declaring the two foreign keys means naming those two entity
/// types, which is one of the listed exceptions to the module rule (ADR 0003, amendment of 2026-11-20): this class
/// names them for the keys and uses nothing else of them. No other class of the Billing module may.
/// </para>
/// </summary>
public sealed class BillConfiguration : IEntityTypeConfiguration<Bill>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        // The additive identities are checked here as well as in the aggregate; the multiplications of BR-202 are
        // not, because SQL Server rounds differently in edge cases (docs/database.md, section 3.3).
        builder.ToTable("Bills", table =>
        {
            table.HasCheckConstraint("CK_Bills_DueDate", "[DueDate] >= [BillDate]");
            table.HasCheckConstraint("CK_Bills_SubTotal", "[SubTotal] >= 0");
            table.HasCheckConstraint("CK_Bills_DiscountTotal", "[DiscountTotal] >= 0");
            table.HasCheckConstraint("CK_Bills_TaxTotal", "[TaxTotal] >= 0");
            table.HasCheckConstraint("CK_Bills_Total", "[Total] >= 0 AND [Total] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
            table.HasCheckConstraint("CK_Bills_BalanceDue", "[BalanceDue] BETWEEN 0 AND [Total]");
            table.HasCheckConstraint(
                "CK_Bills_VoidedIffVoided",
                "([Status] = 'Voided' AND [VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL) OR ([Status] <> 'Voided' AND [VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL)");
        });
        builder.HasKey(bill => bill.Id);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(bill => bill.PatientId)
            .HasConstraintName("FK_Bills_PatientId");
        builder.HasIndex(bill => new { bill.TenantId, bill.PatientId }).HasDatabaseName("IX_Bills_TenantId_PatientId");

        // The payer's bills by status, with the two amounts a statement adds up, so that it never reads the rows.
        builder.HasOne<Payer>()
            .WithMany()
            .HasForeignKey(bill => bill.PayerId)
            .HasConstraintName("FK_Bills_PayerId");
        builder.HasIndex(bill => new { bill.TenantId, bill.PayerId, bill.Status })
            .IncludeProperties(bill => new { bill.BalanceDue, bill.Total })
            .HasDatabaseName("IX_Bills_TenantId_PayerId_Status");

        builder.HasIndex(bill => new { bill.TenantId, bill.BillDate }).HasDatabaseName("IX_Bills_TenantId_BillDate");

        // The search of the list by an exact amount (Prompt 11.1; ADR 0036, amended 2026-11-26).
        builder.HasIndex(bill => new { bill.TenantId, bill.Total }).HasDatabaseName("IX_Bills_TenantId_Total");

        // The filter and the order of the list by this column (Prompt 12.2, ADR 0063).
        builder.HasIndex(bill => new { bill.TenantId, bill.BalanceDue }).HasDatabaseName("IX_Bills_TenantId_BalanceDue");

        builder.Property(bill => bill.EncounterReference).HasMaxLength(Bill.EncounterReferenceMaxLength);
        builder.Property(bill => bill.Notes).HasMaxLength(Bill.NotesMaxLength);
        builder.Property(bill => bill.Status).HasMaxLength(StatusMaxLength);
        builder.Property(bill => bill.VoidReason).HasMaxLength(Bill.VoidReasonMaxLength);

        // What posting adds is configured by BillPostingConfiguration, which is applied after this class and names
        // these properties again. Left out here, so that the table of a draft is a migration of its own.
        builder.Ignore(bill => bill.Number);
        builder.Ignore(bill => bill.FiscalYear);
        builder.Ignore(bill => bill.SequenceNumber);
        builder.Ignore(bill => bill.PostedAtUtc);
        builder.Ignore(bill => bill.PostedByUserId);

        // The lines belong to the bill and are loaded, changed and saved with it. A line taken out of a draft is
        // deleted by Entity Framework (ClientCascade); the database itself deletes nothing (ON DELETE NO ACTION).
        builder.HasMany(bill => bill.Lines)
            .WithOne()
            .HasForeignKey(line => line.BillId)
            .HasConstraintName("FK_BillLines_BillId")
            .OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(bill => bill.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
