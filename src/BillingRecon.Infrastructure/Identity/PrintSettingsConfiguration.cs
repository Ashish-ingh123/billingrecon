using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The table of the print settings (ADR 0059; docs/database.md, section 5.11): at most one row for each hospital and
/// kind of document, with the limits of a layout as CHECK constraints, the two a bill may never switch off among them.
/// </summary>
public sealed class PrintSettingsConfiguration : IEntityTypeConfiguration<PrintSettings>
{
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrintSettings> builder)
    {
        builder.ToTable("PrintSettings", table =>
        {
            foreach (var margin in new[] { nameof(PrintSettings.MarginTopMm), nameof(PrintSettings.MarginRightMm), nameof(PrintSettings.MarginBottomMm), nameof(PrintSettings.MarginLeftMm) })
            {
                table.HasCheckConstraint($"CK_PrintSettings_{margin}", $"[{margin}] BETWEEN {PrintLayout.MarginMinMm} AND {PrintLayout.MarginMaxMm}");
            }

            table.HasCheckConstraint("CK_PrintSettings_StationeryTopMm", $"[StationeryTopMm] BETWEEN 0 AND {PrintLayout.StationeryTopMaxMm}");

            // A bill is not printed on a roll, and always shows its SAC codes and its tax in its halves (ADR 0054, ADR 0059).
            table.HasCheckConstraint("CK_PrintSettings_BillPaper", "[DocumentType] <> 'Bill' OR [Paper] <> 'Roll80'");
            table.HasCheckConstraint("CK_PrintSettings_BillShowsTax", "[DocumentType] <> 'Bill' OR ([ShowSacColumn] = 1 AND [ShowTaxSplit] = 1)");
        });
        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.DocumentType).HasMaxLength(EnumMaxLength);
        builder.Property(settings => settings.Paper).HasMaxLength(EnumMaxLength);
        builder.Property(settings => settings.LogoPosition).HasMaxLength(EnumMaxLength);
        builder.Property(settings => settings.LogoSize).HasMaxLength(EnumMaxLength);
        builder.Property(settings => settings.FooterLine).HasMaxLength(PrintLayout.FooterLineMaxLength);
        builder.Property(settings => settings.Terms).HasMaxLength(PrintLayout.TermsMaxLength);
        builder.Ignore(settings => settings.Layout);

        builder.HasIndex(settings => new { settings.TenantId, settings.DocumentType })
            .IsUnique()
            .HasDatabaseName("UX_PrintSettings_TenantId_DocumentType");
    }
}
