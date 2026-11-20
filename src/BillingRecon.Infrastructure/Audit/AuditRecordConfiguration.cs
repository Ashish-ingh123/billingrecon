using BillingRecon.Domain.Audit;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Audit;

/// <summary>
/// The <c>AuditRecords</c> table (docs/database.md, section 3.8; BR-701 to BR-703). Its key is a number the database
/// assigns, the one key in the model that is not a typed id: records are only ever appended, and the number gives the
/// order they were written in. The tenant column with its filter, guard and foreign key, the instant as
/// <c>datetimeoffset(3)</c> and the user id as <c>uniqueidentifier</c> come from the conventions of the context.
/// The two images are JSON of any length.
/// </summary>
public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).UseIdentityColumn();

        builder.Property(record => record.EntityType).HasMaxLength(AuditRecord.EntityTypeMaxLength).IsUnicode(false);
        builder.Property(record => record.DocumentNumber).HasMaxLength(AuditRecord.DocumentNumberMaxLength).IsUnicode(false);
        builder.Property(record => record.Action).HasMaxLength(AuditRecord.ActionMaxLength).IsUnicode(false);
        builder.Property(record => record.BeforeJson).IsUnbounded();
        builder.Property(record => record.AfterJson).IsUnbounded();
        builder.Property(record => record.Reason).HasMaxLength(AuditRecord.ReasonMaxLength);

        // What happened in the tenant over a period; everything one request did (BR-703); and the trail of one
        // record, oldest first, which is the one the API reads (US-43). Each leads with the tenant.
        builder.HasIndex(record => new { record.TenantId, record.OccurredAtUtc }).HasDatabaseName("IX_AuditRecords_TenantId_OccurredAtUtc");
        builder.HasIndex(record => new { record.TenantId, record.CorrelationId }).HasDatabaseName("IX_AuditRecords_TenantId_CorrelationId");
        builder.HasIndex(record => new { record.TenantId, record.EntityType, record.EntityId, record.Id })
            .HasDatabaseName("IX_AuditRecords_TenantId_EntityType_EntityId_Id");
    }
}
