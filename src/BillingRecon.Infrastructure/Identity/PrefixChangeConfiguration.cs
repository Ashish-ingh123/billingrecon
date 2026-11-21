using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The <c>PrefixChanges</c> table (docs/database.md section 3.1; ADR 0033). The id, the tenant, the audit columns, the
/// row version, the text of the enums and their CHECK constraints come from the conventions of the context.
/// </summary>
public sealed class PrefixChangeConfiguration : IEntityTypeConfiguration<PrefixChange>
{
    private const int DocumentTypeMaxLength = 20;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrefixChange> builder)
    {
        builder.ToTable("PrefixChanges", table =>
        {
            foreach (var column in new[] { nameof(PrefixChange.FromPrefix), nameof(PrefixChange.ToPrefix) })
            {
                // The format of every prefix column (ADR 0008), in the binary collation.
                table.HasCheckConstraint(
                    $"CK_PrefixChanges_{column}",
                    $"LEN([{column}]) BETWEEN 1 AND {DocumentNumber.PrefixMaxLength} AND [{column}] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
            }

            table.HasCheckConstraint("CK_PrefixChanges_ChangesThePrefix", "[FromPrefix] <> [ToPrefix] COLLATE Latin1_General_BIN2");
            table.HasCheckConstraint(
                "CK_PrefixChanges_Decided",
                "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'Pending' AND [DecidedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(change => change.Id);

        builder.Property(change => change.DocumentType).HasMaxLength(DocumentTypeMaxLength);
        builder.Property(change => change.FromPrefix).HasMaxLength(DocumentNumber.PrefixMaxLength).IsUnicode(false);
        builder.Property(change => change.ToPrefix).HasMaxLength(DocumentNumber.PrefixMaxLength).IsUnicode(false);
        builder.Property(change => change.Status).HasMaxLength(StatusMaxLength);

        // One change of a document type waits at a time (ADR 0033).
        builder.HasIndex(change => new { change.TenantId, change.DocumentType })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("UX_PrefixChanges_TenantId_DocumentType_Pending");

        // The list, the newest first.
        builder.HasIndex(change => new { change.TenantId, change.CreatedAtUtc })
            .HasDatabaseName("IX_PrefixChanges_TenantId_CreatedAtUtc");
    }
}
