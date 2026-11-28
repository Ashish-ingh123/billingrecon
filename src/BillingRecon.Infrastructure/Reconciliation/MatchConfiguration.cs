using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>Matches</c>, <c>MatchLines</c> and <c>MatchItems</c> tables (docs/database.md §5.16; ADR 0066). A line and an
/// item are in at most one active match: a unique index on each link table, filtered on its own copy of the match's
/// status, <c>IsActive</c> (BR-1029). The items name a payment or refund by kind and id, without a foreign key: the
/// module names no table of Payments (ADR 0065), and a match never changes what it names.
/// </summary>
public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>, IEntityTypeConfiguration<MatchLine>, IEntityTypeConfiguration<MatchItem>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches");
        builder.HasKey(match => match.Id);
        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(match => match.ChannelId)
            .HasConstraintName("FK_Matches_ChannelId");
        builder.HasIndex(match => new { match.TenantId, match.ChannelId, match.Status }).HasDatabaseName("IX_Matches_TenantId_ChannelId_Status");
        builder.Property(match => match.Kind).HasMaxLength(EnumMaxLength);
        builder.Property(match => match.Source).HasMaxLength(EnumMaxLength);
        builder.Property(match => match.Status).HasMaxLength(EnumMaxLength);
        builder.Property(match => match.Reason).HasMaxLength(Match.ReasonMaxLength);

        builder.HasMany(match => match.Lines)
            .WithOne()
            .HasForeignKey(line => line.MatchId)
            .HasConstraintName("FK_MatchLines_MatchId")
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(match => match.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(match => match.Items)
            .WithOne()
            .HasForeignKey(item => item.MatchId)
            .HasConstraintName("FK_MatchItems_MatchId")
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(match => match.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchLine> builder)
    {
        builder.ToTable("MatchLines", table => table.HasCheckConstraint("CK_MatchLines_Amount", "[Amount] > 0"));
        builder.HasKey(line => line.Id);
        builder.HasOne<StatementLine>()
            .WithMany()
            .HasForeignKey(line => line.StatementLineId)
            .HasConstraintName("FK_MatchLines_StatementLineId");
        builder.HasIndex(line => new { line.TenantId, line.StatementLineId })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_MatchLines_TenantId_StatementLineId_Active");
        builder.HasIndex(line => new { line.TenantId, line.MatchId }).HasDatabaseName("IX_MatchLines_TenantId_MatchId");
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchItem> builder)
    {
        builder.ToTable("MatchItems", table => table.HasCheckConstraint("CK_MatchItems_Amount", "[Amount] > 0"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ItemType).HasMaxLength(EnumMaxLength);
        builder.HasIndex(item => new { item.TenantId, item.ItemType, item.ItemId })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_MatchItems_TenantId_ItemType_ItemId_Active");
        builder.HasIndex(item => new { item.TenantId, item.MatchId }).HasDatabaseName("IX_MatchItems_TenantId_MatchId");
    }
}
