using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The table of the opening batches (ADR 0053; docs/database.md, section 4). The three files are kept as text of any
/// length and out of every audit image: they hold the old numbers of a hospital's patients' bills, and an image of them
/// with every decision would keep them three times.
/// </summary>
public sealed class OpeningBatchConfiguration : IEntityTypeConfiguration<OpeningBatch>
{
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpeningBatch> builder)
    {
        builder.ToTable("OpeningBatches", table =>
        {
            table.HasCheckConstraint("CK_OpeningBatches_Counts", "[Receivables] >= 0 AND [Advances] >= 0 AND [Receivables] + [Advances] >= 1");
            table.HasCheckConstraint("CK_OpeningBatches_Totals", "[ReceivableTotal] >= 0 AND [AdvanceTotal] >= 0");
            table.HasCheckConstraint(
                "CK_OpeningBatches_Decided",
                "([Status] = 'PendingApproval' AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'PendingApproval' AND [DecidedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(batch => batch.Id);

        builder.Property(batch => batch.ContentHash).HasMaxLength(OpeningBatch.ContentHashLength).IsFixedLength().IsUnicode(false);
        builder.Property(batch => batch.Status).HasMaxLength(StatusMaxLength);

        foreach (var file in new[] { nameof(OpeningBatch.BillsCsv), nameof(OpeningBatch.AdvancesCsv), nameof(OpeningBatch.TrialBalanceCsv) })
        {
            builder.Property<string>(file).IsUnbounded().HasAnnotation(EntitySnapshot.NotAuditedAnnotation, true);
        }

        // Opening balances are brought in once (ADR 0053): the same files for the same cut-over date wait or were
        // approved at most once. A rejected batch is left out, so files that were put right can be submitted again.
        builder.HasIndex(batch => new { batch.TenantId, batch.ContentHash })
            .IsUnique()
            .HasFilter("[Status] <> 'Rejected'")
            .HasDatabaseName("UX_OpeningBatches_TenantId_ContentHash");

        // The list, newest first.
        builder.HasIndex(batch => new { batch.TenantId, batch.CreatedAtUtc }).HasDatabaseName("IX_OpeningBatches_TenantId_CreatedAtUtc");
    }
}
