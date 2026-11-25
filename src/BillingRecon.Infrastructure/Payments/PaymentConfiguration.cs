using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The <c>Payments</c> table as recording needs it (docs/database.md, section 3.4; BR-301, BR-302, BR-405). A payment
/// is posted when it is created, so its number is never null. The foreign keys to the payer and to the ledger posting
/// come from the convention of the context for typed-id columns (ADR 0021), so this class names no entity of another
/// module. The columns of a reversal are added by <c>PaymentWithReversalsConfiguration</c> (Prompt 4.5).
/// <para>
/// The allocations of a payment are left out of the model here and put back by
/// <c>PaymentWithAllocationsConfiguration</c>, which Entity Framework applies after this class (configurations
/// are applied in the order of their names), so that the table of payments and the table of allocations are two
/// migrations (<c>0011_Payments</c> and <c>0012_PaymentAllocations</c>) although both were written before either was
/// scaffolded (docs/runbook.md, section 8). Once both migrations exist, the two classes can become one.
/// </para>
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private const int MethodMaxLength = 20;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    private const int OriginMaxLength = 20;

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_SequenceNumber", "[SequenceNumber] >= 1");
            table.HasCheckConstraint(
                "CK_Payments_LegacyNumberIffOpening",
                "([Origin] = 'Standard' AND [LegacyNumber] IS NULL) OR ([Origin] = 'Opening' AND [LegacyNumber] IS NOT NULL)");
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Payments_UnappliedAmount", "[UnappliedAmount] BETWEEN 0 AND [Amount]");
            table.HasCheckConstraint("CK_Payments_Reference", "[Method] = 'Cash' OR [Reference] IS NOT NULL");
        });
        builder.HasKey(payment => payment.Id);

        // Put back by PaymentWithAllocationsConfiguration. Leaving out the navigation is not enough: Entity Framework
        // has discovered the allocation through it by then and would keep it as a table of its own, so the entity
        // is taken out of the model as well, until that class names it.
        builder.Ignore(payment => payment.Allocations);
        var model = builder.Metadata.Model;
        if (model.FindEntityType(typeof(PaymentAllocation)) is { } discovered)
        {
            model.RemoveEntityType(discovered);
        }

        model.AddIgnored(typeof(PaymentAllocation));

        // What a reversal adds is configured by PaymentWithReversalsConfiguration, which is applied after this class and
        // names these properties again. Left out here, so that the table of a recorded payment is a migration of its own.
        builder.Ignore(payment => payment.ReversedAtUtc);
        builder.Ignore(payment => payment.ReversedByUserId);
        builder.Ignore(payment => payment.ReversalReason);
        builder.Ignore(payment => payment.ReversalPostingId);

        // The gapless number (BR-801): unique in the tenant, and one payment per position of the series of a fiscal year.
        builder.Property(payment => payment.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.HasIndex(payment => new { payment.TenantId, payment.Number })
            .IsUnique()
            .HasDatabaseName("UX_Payments_TenantId_Number");
        // The origin is part of the position since ADR 0053: an opening advance has a series of its own.
        builder.HasIndex(payment => new { payment.TenantId, payment.Origin, payment.FiscalYear, payment.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("UX_Payments_TenantId_Origin_FiscalYear_SequenceNumber");

        // Where the payment comes from (ADR 0053). Every payment before the column was recorded here, and so is every
        // payment a user records: the default says so. An opening advance carries the reference it had in the system
        // before, and an old reference is brought in once.
        builder.Property(payment => payment.Origin).HasMaxLength(OriginMaxLength).HasDefaultValue(DocumentOrigin.Standard);
        builder.Property(payment => payment.LegacyNumber).HasMaxLength(Payment.LegacyNumberMaxLength);
        builder.HasIndex(payment => new { payment.TenantId, payment.LegacyNumber })
            .IsUnique()
            .HasFilter("[LegacyNumber] IS NOT NULL")
            .HasDatabaseName("UX_Payments_TenantId_LegacyNumber");

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
