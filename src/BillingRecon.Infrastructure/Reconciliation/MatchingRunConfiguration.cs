using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>MatchingRuns</c> table (ADR 0066): one run under way for a channel at a time, kept by a unique index on the
/// running rows (BR-1029).
/// </summary>
public sealed class MatchingRunConfiguration : IEntityTypeConfiguration<MatchingRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchingRun> builder)
    {
        builder.ToTable("MatchingRuns");
        builder.HasKey(run => run.Id);
        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(run => run.ChannelId)
            .HasConstraintName("FK_MatchingRuns_ChannelId");
        builder.Property(run => run.Status).HasMaxLength(20);
        builder.HasIndex(run => new { run.TenantId, run.ChannelId })
            .IsUnique()
            .HasFilter("[Status] = 'Running'")
            .HasDatabaseName("UX_MatchingRuns_TenantId_ChannelId_Running");
    }
}
