using BillingRecon.Domain.Receivables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The <c>LedgerEntries</c> table (docs/database.md, section 3.7; BR-401 to BR-403). The key is a number the database
/// assigns. The CHECK constraints repeat what <see cref="LedgerPosting"/> checks: an amount on exactly one side, and
/// the subject rules of the six accounts. That the debits of a posting equal its credits spans rows and is not a
/// constraint: the posting checks it before the insert, and the reconciliation query afterwards. The two covering
/// indexes are the ones the balance identities read (section 5.5). The foreign key to the payer comes from the
/// convention of the context for typed-id columns (ADR 0021).
/// </summary>
public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    private const int AccountMaxLength = 20;
    private const int SubjectTypeMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries", table =>
        {
            table.HasCheckConstraint("CK_LedgerEntries_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_LedgerEntries_Debit", "[Debit] >= 0");
            table.HasCheckConstraint("CK_LedgerEntries_Credit", "[Credit] >= 0");
            table.HasCheckConstraint("CK_LedgerEntries_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Debit] = 0 AND [Credit] > 0)");
            table.HasCheckConstraint(
                "CK_LedgerEntries_Subject",
                "([Account] = 'Receivable' AND [SubjectType] = 'Bill' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) "
                + "OR ([Account] = 'UnappliedCash' AND [SubjectType] = 'Payment' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) "
                + "OR ([Account] = 'CreditAvailable' AND [SubjectType] = 'CreditNote' AND [SubjectId] IS NOT NULL AND [PayerId] IS NOT NULL) "
                + "OR ([Account] IN ('Revenue', 'Cash', 'Adjustments') AND [SubjectType] IS NULL AND [SubjectId] IS NULL AND [PayerId] IS NULL)");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();

        builder.Property(entry => entry.Account).HasMaxLength(AccountMaxLength);
        builder.Property(entry => entry.SubjectType).HasMaxLength(SubjectTypeMaxLength);

        // One entry per position of a posting. The index leads with the tenant, like every index of a tenant-scoped
        // table, and also serves the foreign key to the posting.
        builder.HasIndex(entry => new { entry.TenantId, entry.PostingId, entry.LineNo })
            .IsUnique()
            .HasDatabaseName("UX_LedgerEntries_TenantId_PostingId_LineNo");

        // The balance of one bill, payment or credit note, without reading the rows (identities 3 to 5).
        builder.HasIndex(entry => new { entry.TenantId, entry.SubjectType, entry.SubjectId })
            .IncludeProperties(entry => new { entry.Account, entry.Debit, entry.Credit })
            .HasDatabaseName("IX_LedgerEntries_TenantId_SubjectType_SubjectId");

        // The three balances of one payer, without reading the rows (identities 6 to 8).
        builder.HasIndex(entry => new { entry.TenantId, entry.PayerId, entry.Account })
            .IncludeProperties(entry => new { entry.Debit, entry.Credit })
            .HasDatabaseName("IX_LedgerEntries_TenantId_PayerId_Account");
    }
}
