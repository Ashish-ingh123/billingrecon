using BillingRecon.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The <c>Tenants</c> table (docs/database.md, section 3.1). The id, the row version, the storage of the status as
/// text and its CHECK constraint come from the conventions of the context; only what is particular to this table is
/// configured here. Entity Framework creates the instance, which is why the class is public.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    private const int StatusMaxLength = 20;

    private static readonly string[] PrefixColumns =
    [
        nameof(Tenant.BillPrefix),
        nameof(Tenant.CreditNotePrefix),
        nameof(Tenant.PaymentPrefix),
        nameof(Tenant.AdjustmentPrefix),
        nameof(Tenant.RefundPrefix),
    ];

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", table =>
        {
            table.HasCheckConstraint("CK_Tenants_MaxBackdateDays", $"[MaxBackdateDays] BETWEEN 0 AND {Tenant.MaxDays}");
            table.HasCheckConstraint("CK_Tenants_DefaultDueDays", $"[DefaultDueDays] BETWEEN 0 AND {Tenant.MaxDays}");

            foreach (var column in PrefixColumns)
            {
                // One to three characters, capital letters and digits only (ADR 0008). The binary collation makes
                // the range A-Z mean exactly the capital letters, whatever the collation of the database.
                table.HasCheckConstraint(
                    $"CK_Tenants_{column}",
                    $"LEN([{column}]) BETWEEN 1 AND {Tenant.PrefixMaxLength} AND [{column}] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN2");
            }
        });

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Code).HasMaxLength(Tenant.CodeMaxLength).IsUnicode(false);
        builder.HasIndex(tenant => tenant.Code).IsUnique().HasDatabaseName("UX_Tenants_Code");

        builder.Property(tenant => tenant.Name).HasMaxLength(Tenant.NameMaxLength);
        builder.Property(tenant => tenant.Status).HasMaxLength(StatusMaxLength);

        // PostingApprovalThreshold is Money: decimal(18,2) by the convention of the context, like every amount.

        foreach (var column in PrefixColumns)
        {
            builder.Property<string>(column).HasMaxLength(Tenant.PrefixMaxLength).IsUnicode(false);
        }
    }
}
