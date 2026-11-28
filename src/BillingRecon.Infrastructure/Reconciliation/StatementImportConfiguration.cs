using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>StatementImports</c> table (docs/database.md §5.16; ADR 0065): insert-only, like the ledger (the context refuses
/// an update and migration 0036 denies the login UPDATE and DELETE). The same file is imported once per channel: a
/// unique index on the channel and the file's hash.
/// </summary>
public sealed class StatementImportConfiguration : IEntityTypeConfiguration<StatementImport>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StatementImport> builder)
    {
        builder.ToTable("StatementImports", table =>
            table.HasCheckConstraint("CK_StatementImports_Counts", "[RowsRead] = [LinesAdded] + [LinesAlreadyHeld] + [RowsRefused] AND [LinesAdded] >= 0 AND [LinesAlreadyHeld] >= 0 AND [RowsRefused] >= 0"));
        builder.HasKey(import => import.Id);

        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(import => import.ChannelId)
            .HasConstraintName("FK_StatementImports_ChannelId");
        builder.Property(import => import.FileName).HasMaxLength(StatementImport.FileNameMaxLength);
        builder.Property(import => import.FileHash).HasMaxLength(StatementImport.FileHashLength).IsFixedLength().IsUnicode(false);
        builder.HasIndex(import => new { import.TenantId, import.ChannelId, import.FileHash })
            .IsUnique()
            .HasDatabaseName("UX_StatementImports_TenantId_ChannelId_FileHash");
        builder.HasIndex(import => new { import.TenantId, import.CreatedAtUtc }).HasDatabaseName("IX_StatementImports_TenantId_CreatedAtUtc");
    }
}
