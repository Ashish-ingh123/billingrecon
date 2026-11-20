using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Common.Numbering;

/// <summary>
/// The <c>NumberSeries</c> table (docs/database.md, section 3.6; ADR 0008). The id, the tenant column with its filter,
/// guard and foreign key, the row version, the fiscal year as <c>char(7)</c> and the document type as text with its
/// CHECK constraint come from the conventions of the context. The unique index is the row that
/// <see cref="NumberSeriesAllocator"/> locks: it must stay exactly these three columns, in this order.
/// </summary>
public sealed class NumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    private const int DocumentTypeMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NumberSeries> builder)
    {
        builder.ToTable("NumberSeries", table =>
        {
            // One to three characters, capital letters and digits only (ADR 0008), as for the tenant's default
            // prefixes. The binary collation makes the range A-Z mean exactly the capital letters.
            table.HasCheckConstraint(
                "CK_NumberSeries_Prefix",
                $"LEN([Prefix]) BETWEEN 1 AND {DocumentNumber.PrefixMaxLength} AND [Prefix] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
            table.HasCheckConstraint("CK_NumberSeries_NextNumber", "[NextNumber] >= 1");
        });
        builder.HasKey(series => series.Id);

        builder.Property(series => series.DocumentType).HasMaxLength(DocumentTypeMaxLength);
        builder.Property(series => series.Prefix).HasMaxLength(DocumentNumber.PrefixMaxLength).IsUnicode(false);

        builder.HasIndex(series => new { series.TenantId, series.DocumentType, series.FiscalYear })
            .IsUnique()
            .HasDatabaseName("UX_NumberSeries_TenantId_DocumentType_FiscalYear");
    }
}
