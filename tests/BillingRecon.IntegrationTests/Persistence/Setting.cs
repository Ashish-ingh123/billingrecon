using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>A test-only row that belongs to no tenant and is not audited, with one deliberately unbounded column.</summary>
public sealed class Setting(string key) : Entity<SettingId>
{
    public string Key { get; private set; } = key;

    public string? Json { get; private set; }

    public void Store(string json) => Json = json;
}
