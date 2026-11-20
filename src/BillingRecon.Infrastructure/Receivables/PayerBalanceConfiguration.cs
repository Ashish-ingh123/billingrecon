using BillingRecon.Domain.Receivables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The <c>PayerBalances</c> table (docs/database.md, section 3.7; BR-404, BR-405): one row per payer, keyed by the
/// payer. No row version: the ledger changes a row with one locked statement per posting, so two postings for one
/// payer queue on the row instead of failing each other. <c>NetOutstanding</c> is computed and stored by the
/// database. No amount may be negative; the CHECK constraints are the last line of defence behind the ledger. The
/// foreign key to the payer, one row for one payer, comes from the convention of the context for typed-id columns
/// (ADR 0021).
/// </summary>
public sealed class PayerBalanceConfiguration : IEntityTypeConfiguration<PayerBalance>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PayerBalance> builder)
    {
        builder.ToTable("PayerBalances", table =>
        {
            table.HasCheckConstraint("CK_PayerBalances_Receivable", "[Receivable] >= 0");
            table.HasCheckConstraint("CK_PayerBalances_UnappliedCash", "[UnappliedCash] >= 0");
            table.HasCheckConstraint("CK_PayerBalances_CreditAvailable", "[CreditAvailable] >= 0");
        });
        builder.HasKey(balance => balance.PayerId);

        // The key is the payer's own id, given when the row is opened; nothing generates it.
        builder.Property(balance => balance.PayerId).ValueGeneratedNever();

        builder.Property(balance => balance.NetOutstanding)
            .HasComputedColumnSql("[Receivable] - [UnappliedCash] - [CreditAvailable]", stored: true);
    }
}
