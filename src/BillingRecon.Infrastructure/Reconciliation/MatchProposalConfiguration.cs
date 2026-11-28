using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>MatchProposals</c> and <c>MatchProposalItems</c> tables (docs/database.md §5.16; ADR 0066): the open proposals
/// of a channel's lines are what a run withdraws and the lists show.
/// </summary>
public sealed class MatchProposalConfiguration : IEntityTypeConfiguration<MatchProposal>, IEntityTypeConfiguration<MatchProposalItem>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchProposal> builder)
    {
        builder.ToTable("MatchProposals", table =>
        {
            table.HasCheckConstraint("CK_MatchProposals_Rule", "[Rule] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_MatchProposals_Score", "[Score] BETWEEN 0 AND 100");
        });
        builder.HasKey(proposal => proposal.Id);
        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(proposal => proposal.ChannelId)
            .HasConstraintName("FK_MatchProposals_ChannelId");
        builder.HasOne<StatementLine>()
            .WithMany()
            .HasForeignKey(proposal => proposal.StatementLineId)
            .HasConstraintName("FK_MatchProposals_StatementLineId");
        builder.HasIndex(proposal => new { proposal.TenantId, proposal.ChannelId, proposal.Status }).HasDatabaseName("IX_MatchProposals_TenantId_ChannelId_Status");
        builder.HasIndex(proposal => new { proposal.TenantId, proposal.StatementLineId, proposal.Status }).HasDatabaseName("IX_MatchProposals_TenantId_StatementLineId_Status");
        builder.Property(proposal => proposal.Kind).HasMaxLength(EnumMaxLength);
        builder.Property(proposal => proposal.Status).HasMaxLength(EnumMaxLength);
        builder.Property(proposal => proposal.Reason).HasMaxLength(MatchProposal.ReasonMaxLength);

        builder.HasMany(proposal => proposal.Items)
            .WithOne()
            .HasForeignKey(item => item.ProposalId)
            .HasConstraintName("FK_MatchProposalItems_ProposalId")
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(proposal => proposal.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchProposalItem> builder)
    {
        builder.ToTable("MatchProposalItems", table => table.HasCheckConstraint("CK_MatchProposalItems_Amount", "[Amount] > 0"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ItemType).HasMaxLength(EnumMaxLength);
        builder.HasIndex(item => new { item.TenantId, item.ProposalId }).HasDatabaseName("IX_MatchProposalItems_TenantId_ProposalId");
    }
}
