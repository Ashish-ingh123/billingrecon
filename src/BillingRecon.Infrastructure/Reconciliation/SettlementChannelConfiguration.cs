using System.Text.Json;
using BillingRecon.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>
/// The <c>SettlementChannels</c> table (docs/database.md §5.16; ADR 0065). The id, the tenant, the audit columns, the row
/// version and the enums as text with their CHECK constraints come from the conventions of the context. The column
/// mapping is JSON in one column; the name is unique in the tenant, which the database compares without regard to case.
/// </summary>
public sealed class SettlementChannelConfiguration : IEntityTypeConfiguration<SettlementChannel>
{
    private const int EnumMaxLength = 20;

    /// <summary>The longest JSON a mapping writes: eight names of 100 characters, and room for the rest.</summary>
    private const int MappingMaxLength = 2000;

    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettlementChannel> builder)
    {
        // A bank has a mapping and no gateway or bank; a gateway the opposite (BR-1001). The window is 0 to 10 days.
        builder.ToTable("SettlementChannels", table =>
        {
            table.HasCheckConstraint(
                "CK_SettlementChannels_Kind",
                "([Kind] = 'Bank' AND [Mapping] IS NOT NULL AND [Gateway] IS NULL AND [BankChannelId] IS NULL) OR ([Kind] = 'Gateway' AND [Mapping] IS NULL AND [Gateway] IS NOT NULL AND [BankChannelId] IS NOT NULL)");
            table.HasCheckConstraint("CK_SettlementChannels_DateWindowDays", $"[DateWindowDays] BETWEEN 0 AND {SettlementChannel.MaxDateWindowDays}");
        });
        builder.HasKey(channel => channel.Id);

        builder.Property(channel => channel.Kind).HasMaxLength(EnumMaxLength);
        builder.Property(channel => channel.Name).HasMaxLength(SettlementChannel.NameMaxLength);
        builder.HasIndex(channel => new { channel.TenantId, channel.Name })
            .IsUnique()
            .HasDatabaseName("UX_SettlementChannels_TenantId_Name");
        builder.Property(channel => channel.AccountLabel).HasMaxLength(SettlementChannel.AccountLabelMaxLength).IsUnicode(false);
        builder.Property(channel => channel.Gateway).HasMaxLength(EnumMaxLength);
        builder.Property(channel => channel.Status).HasMaxLength(EnumMaxLength);

        // The bank a gateway settles into: a channel of the same table.
        builder.HasOne<SettlementChannel>()
            .WithMany()
            .HasForeignKey(channel => channel.BankChannelId)
            .HasConstraintName("FK_SettlementChannels_BankChannelId");
        builder.HasIndex(channel => new { channel.TenantId, channel.BankChannelId }).HasDatabaseName("IX_SettlementChannels_TenantId_BankChannelId");

        builder.Property(channel => channel.Mapping)
            .HasConversion(
                mapping => mapping.HasValue ? JsonSerializer.Serialize(mapping.Value, Json) : null,
                text => text == null ? null : JsonSerializer.Deserialize<ColumnMapping>(text, Json))
            .HasMaxLength(MappingMaxLength);
    }
}
