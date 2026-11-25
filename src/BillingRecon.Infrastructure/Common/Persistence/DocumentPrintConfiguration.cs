using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The table of the recorded prints (ADR 0054; docs/database.md, section 5.9): insert-only, one row a print, the copy
/// numbers of a document gapless from 1 by the unique index, which also answers how many prints a document has.
/// </summary>
public sealed class DocumentPrintConfiguration : IEntityTypeConfiguration<DocumentPrint>
{
    private const int DocumentTypeMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DocumentPrint> builder)
    {
        builder.ToTable("DocumentPrints", table => table.HasCheckConstraint("CK_DocumentPrints_CopyNumber", "[CopyNumber] >= 1"));
        builder.HasKey(print => print.Id);

        builder.Property(print => print.DocumentType).HasMaxLength(DocumentTypeMaxLength);
        builder.Ignore(print => print.IsOriginal);

        // One original, then the duplicates in their order: two prints at the same instant cannot take the same number.
        builder.HasIndex(print => new { print.TenantId, print.DocumentType, print.DocumentId, print.CopyNumber })
            .IsUnique()
            .HasDatabaseName("UX_DocumentPrints_TenantId_DocumentType_DocumentId_CopyNumber");
    }
}
