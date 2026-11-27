using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The table of a hospital's names for its accountant's ledgers (ADR 0062): one row for a tenant, a column for each
/// ledger the export names.
/// </summary>
public sealed class AccountingLedgersConfiguration : IEntityTypeConfiguration<AccountingLedgers>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AccountingLedgers> builder)
    {
        builder.ToTable("AccountingLedgers");
        builder.HasKey(ledgers => ledgers.Id);

        builder.Property(ledgers => ledgers.Receivable).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.UnappliedCash).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.CreditAvailable).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Revenue).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.TaxOutput).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Adjustments).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.OpeningBalances).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Cash).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Card).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Upi).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.BankTransfer).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Property(ledgers => ledgers.Cheque).HasMaxLength(AccountingLedgerNames.NameMaxLength);
        builder.Ignore(ledgers => ledgers.Names);

        builder.HasIndex(ledgers => ledgers.TenantId)
            .IsUnique()
            .HasDatabaseName("UX_AccountingLedgers_TenantId");
    }
}
