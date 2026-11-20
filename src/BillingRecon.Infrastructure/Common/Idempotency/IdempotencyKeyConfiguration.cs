using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Common.Idempotency;

/// <summary>
/// The <c>IdempotencyKeys</c> table (docs/database.md, sections 3.6 and 6; ADR 0006). The primary key is the tenant
/// and the key, which is also the clustered index: the one lookup the table serves. The tenant column with its filter,
/// guard and foreign key, the instants as <c>datetimeoffset(3)</c> and the status as text with its CHECK constraint
/// come from the conventions of the context.
/// </summary>
public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    private const int StatusMaxLength = 12;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys", table =>
        {
            // A key in progress has no answer; a completed one has its status and the instant it was stored.
            table.HasCheckConstraint(
                "CK_IdempotencyKeys_Completed",
                "([Status] = 'Completed' AND [StatusCode] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [ResponseBody] IS NOT NULL) OR ([Status] = 'InProgress' AND [StatusCode] IS NULL AND [CompletedAtUtc] IS NULL AND [ResponseBody] IS NULL)");
        });
        builder.HasKey(key => new { key.TenantId, key.Key });

        // Neither part of the key is generated: the key is the client's, and the tenant is set by the tenant guard
        // when the row is added, like the tenant of every other row. (Until the guard has run, Entity Framework
        // tracks the row under a temporary tenant, which the guard knows to replace.)
        builder.Property(key => key.TenantId).ValueGeneratedNever();
        builder.Property(key => key.Key).ValueGeneratedNever();

        builder.Property(key => key.Method).HasMaxLength(IdempotencyKey.MethodMaxLength).IsUnicode(false);
        builder.Property(key => key.Path).HasMaxLength(IdempotencyKey.PathMaxLength);
        builder.Property(key => key.RequestHash).HasMaxLength(IdempotencyKey.RequestHashLength).IsFixedLength();
        builder.Property(key => key.Status).HasMaxLength(StatusMaxLength);
        builder.Property(key => key.ResponseContentType).HasMaxLength(IdempotencyKey.ResponseContentTypeMaxLength).IsUnicode(false);
        builder.Property(key => key.ResponseHeaders).HasMaxLength(IdempotencyKey.ResponseHeadersMaxLength);
        builder.Property(key => key.ResponseBody).IsUnbounded();

        // For the sweep of expired keys, tenant by tenant: like every index of a tenant-scoped table it leads with
        // the tenant.
        builder.HasIndex(key => new { key.TenantId, key.ExpiresAtUtc }).HasDatabaseName("IX_IdempotencyKeys_TenantId_ExpiresAtUtc");
    }
}
