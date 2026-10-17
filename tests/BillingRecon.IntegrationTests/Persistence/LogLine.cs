using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>A test-only append-only row: tenant-scoped, creation audit only, no row version.</summary>
public sealed class LogLine(string text) : Entity<LogLineId>, ITenantScoped, ICreationAudited
{
    public string Text { get; private set; } = text;

    public TenantId TenantId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public UserId? CreatedByUserId { get; private set; }

    public void Rewrite(string text) => Text = text;
}
