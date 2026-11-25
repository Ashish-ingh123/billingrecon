using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// What posting adds to the <c>Bills</c> table (docs/database.md, section 3.3; BR-206, BR-208, BR-801): the gapless
/// number with its fiscal year and its position in the series, who posted the bill and when, and the constraints
/// that tie the number and the balance to the status.
/// <para>
/// It is a class of its own, beside <see cref="BillConfiguration"/>, so that the table of a draft and the columns of
/// a posted bill are two migrations (<c>0004_Bills</c> and <c>0009_BillPosting</c>) although both were written
/// before either was scaffolded: <see cref="BillConfiguration"/> leaves these five properties out of the model, and
/// this class, which Entity Framework applies after it (configurations are applied in the order of their names),
/// puts them in. Scaffolding <c>0004_Bills</c> with this file moved out of the project therefore gives the table as
/// drafts need it (docs/runbook.md, section 8). Once both migrations exist, the two classes can become one.
/// </para>
/// </summary>
public sealed class BillPostingConfiguration : IEntityTypeConfiguration<Bill>
{
    /// <inheritdoc />
    private const int OriginMaxLength = 20;

    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        builder.ToTable("Bills", table =>
        {
            // PendingApproval is named already, for the status maker-checker adds (BR-207): a bill that waits has no number.
            table.HasCheckConstraint(
                "CK_Bills_NumberIffPosted",
                "([Status] IN ('Draft', 'PendingApproval', 'Voided') AND [Number] IS NULL AND [PostedAtUtc] IS NULL) OR ([Status] IN ('Posted', 'PartiallyPaid', 'Settled') AND [Number] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Bills_NumberParts",
                "([Number] IS NULL AND [FiscalYear] IS NULL AND [SequenceNumber] IS NULL AND [PostedByUserId] IS NULL) OR ([Number] IS NOT NULL AND [FiscalYear] IS NOT NULL AND [SequenceNumber] >= 1)");
            table.HasCheckConstraint(
                "CK_Bills_LegacyNumberIffOpening",
                "([Origin] = 'Standard' AND [LegacyNumber] IS NULL) OR ([Origin] = 'Opening' AND [LegacyNumber] IS NOT NULL AND [Number] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Bills_StatusMatchesBalance",
                "([Status] <> 'Posted' OR ([BalanceDue] = [Total] AND [Total] > 0)) AND ([Status] <> 'PartiallyPaid' OR ([BalanceDue] > 0 AND [BalanceDue] < [Total])) AND ([Status] <> 'Settled' OR [BalanceDue] = 0)");
        });

        // Naming a property here is what brings it back into the model after BillConfiguration left it out.
        builder.Property(bill => bill.Number).HasMaxLength(DocumentNumber.MaxLength).IsUnicode(false);
        builder.Property(bill => bill.FiscalYear);
        builder.Property(bill => bill.SequenceNumber);
        builder.Property(bill => bill.PostedAtUtc);
        builder.Property(bill => bill.PostedByUserId);

        // The gapless number of a posted bill (BR-801): unique in the tenant, and one bill per position of the series
        // of a fiscal year. Drafts and voided drafts have neither, so both indexes leave them out.
        builder.HasIndex(bill => new { bill.TenantId, bill.Number })
            .IsUnique()
            .HasFilter("[Number] IS NOT NULL")
            .HasDatabaseName("UX_Bills_TenantId_Number");
        // The origin is part of the position since ADR 0053: an opening receivable has a series of its own, whose first
        // number in a fiscal year is 1 like the first bill's.
        builder.HasIndex(bill => new { bill.TenantId, bill.Origin, bill.FiscalYear, bill.SequenceNumber })
            .IsUnique()
            .HasFilter("[SequenceNumber] IS NOT NULL")
            .HasDatabaseName("UX_Bills_TenantId_Origin_FiscalYear_SequenceNumber");

        // Where the bill comes from (ADR 0053). Every bill before the column was raised here, and so is every bill a
        // user raises: the default says so. An opening receivable carries the number it had in the system before, and an
        // old number is brought in once.
        builder.Property(bill => bill.Origin).HasMaxLength(OriginMaxLength).HasDefaultValue(DocumentOrigin.Standard);
        builder.Property(bill => bill.LegacyNumber).HasMaxLength(Bill.LegacyNumberMaxLength);
        builder.HasIndex(bill => new { bill.TenantId, bill.LegacyNumber })
            .IsUnique()
            .HasFilter("[LegacyNumber] IS NOT NULL")
            .HasDatabaseName("UX_Bills_TenantId_LegacyNumber");
    }
}
