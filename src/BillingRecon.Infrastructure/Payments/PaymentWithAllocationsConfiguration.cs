using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The <c>PaymentAllocations</c> table as allocating needs it (docs/database.md, section 3.4; BR-303, BR-304), and
/// the relationship that makes the allocations part of their payment. An allocation has no row version of its own:
/// the payment's covers it. The foreign keys to the bill and to the ledger posting come from the convention of the
/// context for typed-id columns (ADR 0021), so this class names no entity of another module. The columns and states
/// of a reversal arrive with reversals (Prompt 4.5).
/// <para>
/// One class for both entities, in a file of its own, named so that Entity Framework applies it after
/// <see cref="PaymentConfiguration"/> (configurations are applied in the order of their names): that class leaves
/// the allocations of a payment out of the model, and this one puts them in. Scaffolding <c>0011_Payments</c> with
/// this file moved out of the project therefore gives the table of payments alone, and
/// <c>0012_PaymentAllocations</c> the rest (docs/runbook.md, section 8).
/// </para>
/// </summary>
public sealed class PaymentWithAllocationsConfiguration : IEntityTypeConfiguration<Payment>, IEntityTypeConfiguration<PaymentAllocation>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // Naming the navigation here is what brings it back into the model after PaymentConfiguration left it out.
        builder.HasMany(payment => payment.Allocations)
            .WithOne()
            .HasForeignKey(allocation => allocation.PaymentId)
            .HasConstraintName("FK_PaymentAllocations_PaymentId")
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(payment => payment.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations", table => table.HasCheckConstraint("CK_PaymentAllocations_Amount", "[Amount] > 0"));
        builder.HasKey(allocation => allocation.Id);

        // The allocations of a payment; also serves the foreign key to the payment.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.PaymentId })
            .HasDatabaseName("IX_PaymentAllocations_TenantId_PaymentId");

        // What was applied to a bill, without reading the rows.
        builder.HasIndex(allocation => new { allocation.TenantId, allocation.BillId })
            .IncludeProperties(allocation => new { allocation.Amount, allocation.Status })
            .HasDatabaseName("IX_PaymentAllocations_TenantId_BillId");

        builder.Property(allocation => allocation.Status).HasMaxLength(StatusMaxLength);

        // Put back by PaymentWithReversalsConfiguration, so that the columns of a reversal are a migration of their own.
        builder.Ignore(allocation => allocation.ReversedAtUtc);
        builder.Ignore(allocation => allocation.ReversedByUserId);
        builder.Ignore(allocation => allocation.ReversalReason);
        builder.Ignore(allocation => allocation.ReversalPostingId);
        builder.Ignore(allocation => allocation.IsApplied);
    }
}
