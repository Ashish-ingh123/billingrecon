using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>CreditNoteAllocations</c> table (docs/database.md, section 3.3; BR-212, BR-213, ADR 0028): no row version of
/// its own, the credit note's covers it. The foreign keys to the bill, to the ledger posting and to the posting of a
/// reversal come from the convention for typed-id columns (ADR 0021). The columns of a reversal and the flag of the
/// application made at posting arrived with Prompt 4.5b, in migration <c>0016_CreditApplicationReversals</c>.
/// </summary>
public sealed class CreditNoteAllocationConfiguration : IEntityTypeConfiguration<CreditNoteAllocation>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreditNoteAllocation> builder)
    {
        builder.ToTable("CreditNoteAllocations", table =>
        {
            table.HasCheckConstraint("CK_CreditNoteAllocations_Amount", "[Amount] > 0");

            // The four columns of a reversal are set exactly when the application is reversed (ADR 0028).
            table.HasCheckConstraint(
                "CK_CreditNoteAllocations_ReversedIffReversed",
                "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)");

            // The application a credit note made to its own bill when it was posted is never reversed (ADR 0028).
            table.HasCheckConstraint("CK_CreditNoteAllocations_AtPostingStaysActive", "[AppliedAtPosting] = 0 OR [Status] = 'Active'");
        });
        builder.HasKey(allocation => allocation.Id);

        // The applications of a credit note; also serves the foreign key to the credit note.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.CreditNoteId })
            .HasDatabaseName("IX_CreditNoteAllocations_TenantId_CreditNoteId");

        // What was applied to a bill, without reading the rows.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.BillId })
            .IncludeProperties(allocation => new { allocation.Amount, allocation.Status })
            .HasDatabaseName("IX_CreditNoteAllocations_TenantId_BillId");

        builder.Property(allocation => allocation.Status).HasMaxLength(StatusMaxLength);
        builder.Property(allocation => allocation.ReversalReason).HasMaxLength(CreditNote.ReasonMaxLength);
    }
}
