using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>StatementLines</c> table (docs/database.md §5.16; ADR 0065). A line is never imported twice: a unique index on
/// the channel, the fingerprint and the occurrence (BR-1004). The amount is above zero (BR-1005). The lists read the lines
/// of a channel by date and by status.
/// </summary>
public sealed class StatementLineConfiguration : IEntityTypeConfiguration<StatementLine>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StatementLine> builder)
    {
        builder.ToTable("StatementLines", table =>
        {
            table.HasCheckConstraint("CK_StatementLines_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_StatementLines_RowNumber", "[RowNumber] >= 1 AND [Occurrence] >= 1");
        });
        builder.HasKey(line => line.Id);

        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(line => line.ChannelId)
            .HasConstraintName("FK_StatementLines_ChannelId");
        builder.HasOne<StatementImport>()
            .WithMany()
            .HasForeignKey(line => line.ImportId)
            .HasConstraintName("FK_StatementLines_ImportId");

        builder.Property(line => line.Direction).HasMaxLength(EnumMaxLength);
        builder.Property(line => line.Status).HasMaxLength(EnumMaxLength);
        builder.Property(line => line.Reference).HasMaxLength(StatementLine.ReferenceMaxLength);
        builder.Property(line => line.Description).HasMaxLength(StatementLine.DescriptionMaxLength);
        builder.Property(line => line.RawText).HasMaxLength(StatementLine.RawTextMaxLength);
        builder.Property(line => line.Fingerprint).HasMaxLength(StatementLine.FingerprintLength).IsFixedLength().IsUnicode(false);

        builder.HasIndex(line => new { line.TenantId, line.ChannelId, line.Fingerprint, line.Occurrence })
            .IsUnique()
            .HasDatabaseName("UX_StatementLines_TenantId_ChannelId_Fingerprint_Occurrence");
        builder.HasIndex(line => new { line.TenantId, line.ChannelId, line.Status, line.TransactionDate })
            .HasDatabaseName("IX_StatementLines_TenantId_ChannelId_Status_TransactionDate");
        builder.HasIndex(line => new { line.TenantId, line.ImportId }).HasDatabaseName("IX_StatementLines_TenantId_ImportId");
        builder.HasIndex(line => new { line.TenantId, line.TransactionDate }).HasDatabaseName("IX_StatementLines_TenantId_TransactionDate");
    }
}
