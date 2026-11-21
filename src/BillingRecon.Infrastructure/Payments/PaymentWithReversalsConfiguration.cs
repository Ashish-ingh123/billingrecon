using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// What reversals add to the <c>Payments</c> and <c>PaymentAllocations</c> tables (docs/database.md, section 3.4;
/// BR-305, BR-306): on each, when it was reversed, by whom, why, and the mirror ledger posting, with the constraint
/// that ties the four to the status <c>Reversed</c>. The foreign keys to the reversal postings come from the
/// convention of the context for typed-id columns (ADR 0021).
/// <para>
/// A class of its own, named so that Entity Framework applies it after <see cref="PaymentConfiguration"/> and
/// <see cref="PaymentWithAllocationsConfiguration"/> (configurations are applied in the order of their names): those
/// two leave the reversal properties out of the model, and this one puts them in. Scaffolding <c>0011_Payments</c> and
/// <c>0012_PaymentAllocations</c> with this file moved out of the project therefore gives the two tables as recording
/// and allocating need them, and <c>0015_PaymentReversals</c> the columns (docs/runbook.md, section 8). Once the
/// migrations exist, the three classes can become one.
/// </para>
/// </summary>
public sealed class PaymentWithReversalsConfiguration : IEntityTypeConfiguration<Payment>, IEntityTypeConfiguration<PaymentAllocation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table => table.HasCheckConstraint(
            "CK_Payments_ReversedIffReversed",
            "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL AND [UnappliedAmount] = 0) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)"));

        // Naming a property here is what brings it back into the model after PaymentConfiguration left it out.
        builder.Property(payment => payment.ReversedAtUtc);
        builder.Property(payment => payment.ReversedByUserId);
        builder.Property(payment => payment.ReversalReason).HasMaxLength(Payment.ReversalReasonMaxLength);
        builder.Property(payment => payment.ReversalPostingId);
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations", table => table.HasCheckConstraint(
            "CK_PaymentAllocations_ReversedIffReversed",
            "([Status] = 'Reversed' AND [ReversedAtUtc] IS NOT NULL AND [ReversedByUserId] IS NOT NULL AND [ReversalReason] IS NOT NULL AND [ReversalPostingId] IS NOT NULL) OR ([Status] <> 'Reversed' AND [ReversedAtUtc] IS NULL AND [ReversedByUserId] IS NULL AND [ReversalReason] IS NULL AND [ReversalPostingId] IS NULL)"));

        builder.Property(allocation => allocation.ReversedAtUtc);
        builder.Property(allocation => allocation.ReversedByUserId);
        builder.Property(allocation => allocation.ReversalReason).HasMaxLength(Payment.ReversalReasonMaxLength);
        builder.Property(allocation => allocation.ReversalPostingId);
    }
}
