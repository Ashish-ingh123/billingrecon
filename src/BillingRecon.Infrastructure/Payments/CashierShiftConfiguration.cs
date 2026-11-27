using BillingRecon.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The table of the shifts (docs/database.md; ADR 0060). What the context does by convention is not repeated here:
/// the id, the tenant, the audit columns, the row version, the money columns, the enum as text with its CHECK, and
/// the foreign key of the cashier to the users (ADR 0021, ADR 0030).
/// </summary>
public sealed class CashierShiftConfiguration : IEntityTypeConfiguration<CashierShift>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CashierShift> builder)
    {
        builder.ToTable("CashierShifts", table =>
        {
            table.HasCheckConstraint("CK_CashierShifts_OpeningFloat", "[OpeningFloat] >= 0");
            table.HasCheckConstraint("CK_CashierShifts_CashDocuments", "[CashDocuments] >= 0");
            table.HasCheckConstraint("CK_CashierShifts_LooseChange", "[LooseChange] IS NULL OR ([LooseChange] >= 0 AND [LooseChange] < 1)");

            // An open shift has no count; a counted one has all of it (BR-313).
            table.HasCheckConstraint(
                "CK_CashierShifts_CountedIffNotOpen",
                "([Status] = 'Open' AND [Denominations] IS NULL AND [LooseChange] IS NULL AND [CountedCash] IS NULL AND [ExpectedCash] IS NULL AND [Difference] IS NULL AND [DifferenceReason] IS NULL AND [CountedAtUtc] IS NULL) OR ([Status] <> 'Open' AND [Denominations] IS NOT NULL AND [LooseChange] IS NOT NULL AND [CountedCash] IS NOT NULL AND [ExpectedCash] IS NOT NULL AND [Difference] IS NOT NULL AND [CountedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_CashierShifts_Difference", "[Difference] IS NULL OR [Difference] = [CountedCash] - [ExpectedCash]");

            // A difference has its reason, and nothing else has one (BR-313).
            table.HasCheckConstraint(
                "CK_CashierShifts_ReasonIffDifference",
                "[Difference] IS NULL OR ([Difference] = 0 AND [DifferenceReason] IS NULL) OR ([Difference] <> 0 AND [DifferenceReason] IS NOT NULL)");

            // Only a difference waits for a checker (BR-314).
            table.HasCheckConstraint("CK_CashierShifts_PendingHasDifference", "[Status] <> 'PendingApproval' OR [Difference] <> 0");
            table.HasCheckConstraint(
                "CK_CashierShifts_ClosedIffClosed",
                "([Status] = 'Closed' AND [ClosedAtUtc] IS NOT NULL) OR ([Status] <> 'Closed' AND [ClosedAtUtc] IS NULL)");
        });
        builder.HasKey(shift => shift.Id);

        builder.Property(shift => shift.Status).HasMaxLength(StatusMaxLength);
        builder.Property(shift => shift.DifferenceReason).HasMaxLength(CashierShift.ReasonMaxLength);

        // The count is one value, held as its canonical text (CashCount).
        builder.Property(shift => shift.Denominations)
            .HasConversion(new ValueConverter<CashCount, string>(count => count.Text, text => CashCount.Parse(text)))
            .HasMaxLength(CashCount.TextMaxLength)
            .IsUnicode(false);

        // A user has one shift at a time (BR-310): open, or waiting for approval.
        builder.HasIndex(shift => new { shift.TenantId, shift.CashierUserId })
            .IsUnique()
            .HasFilter("[Status] <> 'Closed'")
            .HasDatabaseName("UX_CashierShifts_TenantId_CashierUserId_Unclosed");

        // The shifts of a business date: the day-end report and the list.
        builder.HasIndex(shift => new { shift.TenantId, shift.BusinessDate })
            .HasDatabaseName("IX_CashierShifts_TenantId_BusinessDate");
    }
}
