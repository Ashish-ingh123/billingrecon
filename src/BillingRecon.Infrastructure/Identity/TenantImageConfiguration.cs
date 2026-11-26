using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The table of the uploaded images of the letterheads (ADR 0059; docs/database.md, section 3.1): insert-only, one row
/// for each different file a hospital uploaded for its logo or its signature, found by the hash its tenant row names.
/// </summary>
public sealed class TenantImageConfiguration : IEntityTypeConfiguration<TenantImage>
{
    private const int KindMaxLength = 20;
    private const int FormatMaxLength = 10;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantImage> builder)
    {
        builder.ToTable("TenantImages", table =>
        {
            table.HasCheckConstraint("CK_TenantImages_Width", $"[Width] BETWEEN 1 AND {TenantImage.MaxSidePixels}");
            table.HasCheckConstraint("CK_TenantImages_Height", $"[Height] BETWEEN 1 AND {TenantImage.MaxSidePixels}");
            table.HasCheckConstraint("CK_TenantImages_Content", $"DATALENGTH([Content]) BETWEEN 1 AND {TenantImage.LogoMaxBytes}");
        });
        builder.HasKey(image => image.Id);

        builder.Property(image => image.Kind).HasMaxLength(KindMaxLength);
        builder.Property(image => image.Format).HasMaxLength(FormatMaxLength);
        builder.Property(image => image.Sha256).HasMaxLength(TenantImage.Sha256Length).IsFixedLength().IsUnicode(false);

        // The same file uploaded again is the row there is: the hash is the image's name within its hospital and kind.
        builder.HasIndex(image => new { image.TenantId, image.Kind, image.Sha256 })
            .IsUnique()
            .HasDatabaseName("UX_TenantImages_TenantId_Kind_Sha256");
    }
}
