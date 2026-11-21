using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The <c>CreditNoteAllocations</c> table as applying credit needs it (docs/database.md, section 3.3; BR-212,
/// BR-213): no row version of its own, the credit note's covers it. The foreign keys to the bill and to the ledger
/// posting come from the convention for typed-id columns (ADR 0021). The columns and states of a reversal are not
/// built: no prompt of R1 reverses an application yet.
/// </summary>
public sealed class CreditNoteAllocationConfiguration : IEntityTypeConfiguration<CreditNoteAllocation>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreditNoteAllocation> builder)
    {
        builder.ToTable("CreditNoteAllocations", table => table.HasCheckConstraint("CK_CreditNoteAllocations_Amount", "[Amount] > 0"));
        builder.HasKey(allocation => allocation.Id);

        // The applications of a credit note; also serves the foreign key to the credit note.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.CreditNoteId })
            .HasDatabaseName("IX_CreditNoteAllocations_TenantId_CreditNoteId");

        // What was applied to a bill, without reading the rows.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.BillId })
            .IncludeProperties(allocation => new { allocation.Amount, allocation.Status })
            .HasDatabaseName("IX_CreditNoteAllocations_TenantId_BillId");

        builder.Property(allocation => allocation.Status).HasMaxLength(StatusMaxLength);
    }
}
