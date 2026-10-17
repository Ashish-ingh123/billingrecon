using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Persistence;
using Bogus;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Realistic test data from one seeded generator: the same tenants and names in every run, so a failing test can be
/// reproduced, and values that look like what the system will hold (Indian place names). A Bogus generator is not
/// thread-safe and test classes run in parallel, so every use takes the lock.
/// </summary>
internal static class TestData
{
    private const int Seed = 20261017;

    private static readonly Lock Sync = new();

    private static readonly Faker Faker = new("en_IND") { Random = new Randomizer(Seed) };

    /// <summary>A valid, active tenant with a code and a name no other call returns, created at <paramref name="createdAtUtc"/>.</summary>
    public static Tenant NewTenant(DateTimeOffset createdAtUtc)
    {
        lock (Sync)
        {
            var city = Faker.Address.City();
            var code = $"{Slug(city)}-{Faker.Random.Number(100, 999)}";
            var name = $"{city} {Faker.PickRandom("Hospital", "Clinic", "Nursing Home", "Diagnostic Centre")}";

            return Tenant.Create(code, name, createdAtUtc).Value;
        }
    }

    /// <summary>A probe widget with a product-like name.</summary>
    public static Widget NewWidget()
    {
        lock (Sync)
        {
            return new Widget(Faker.Commerce.ProductName());
        }
    }

    // Lower-case letters and digits with single hyphens, as Tenant.Create requires, short enough to leave room for the
    // numeric suffix within the 20 characters of a code.
    private static string Slug(string text)
    {
        var characters = text.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-');
        var slug = new string([.. characters]);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length > 15)
        {
            slug = slug[..15].TrimEnd('-');
        }

        return slug.Length > 0 ? slug : "tenant";
    }
}
