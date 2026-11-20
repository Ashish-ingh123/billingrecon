using BillingRecon.Api.Billing;
using BillingRecon.Api.Payers;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Persistence;
using Bogus;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Realistic test data from one seeded generator: the same tenants and names in every run, so a failing test can be
/// reproduced, and values that look like what the system will hold (Indian names and places). A Bogus generator is not
/// thread-safe and test classes run in parallel, so every use takes the lock.
/// </summary>
internal static class TestData
{
    private const int Seed = 20261017;

    private static readonly Lock Sync = new();

    private static readonly Faker Faker = new("en_IND") { Random = new Randomizer(Seed) };

    private static readonly DateTime LatestBirth = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // MRNs are numbered, not drawn: two registrations in one test must never collide by chance (BR-101).
    private static int _lastMrn;

    // Codes are numbered for the same reason: one tenant never has two items with one code.
    private static int _lastServiceItem;

    /// <summary>
    /// A complete registration of a patient with an MRN no other call returns (<c>MRN-000001</c>, <c>MRN-000002</c>, ...):
    /// an Indian name, a date of birth before 2026, a mobile number, an e-mail address and an address with a PIN code.
    /// </summary>
    public static RegisterPatientRequest NewPatient()
    {
        lock (Sync)
        {
            _lastMrn++;

            return new RegisterPatientRequest
            {
                Mrn = $"MRN-{_lastMrn:D6}",
                FullName = Faker.Name.FullName(),
                DateOfBirth = DateOnly.FromDateTime(Faker.Date.Past(80, LatestBirth)),
                Sex = Faker.PickRandom<Sex>(),
                Phone = Faker.Random.Replace("+91 9#### #####"),
                Email = Faker.Internet.Email(),
                AddressLine1 = Faker.Address.StreetAddress(),
                City = Faker.Address.City(),
                State = Faker.Address.State(),
                PinCode = Faker.Random.Replace("4#####"),
            };
        }
    }

    /// <summary>
    /// A complete entry of the service catalogue with a code no other call returns (<c>SVC-000001</c>, <c>SVC-000002</c>,
    /// ...): a name, a category, a price in whole paise and one of the GST rates.
    /// </summary>
    public static CreateServiceItemRequest NewServiceItem()
    {
        lock (Sync)
        {
            _lastServiceItem++;

            return new CreateServiceItemRequest
            {
                Code = $"SVC-{_lastServiceItem:D6}",
                Name = Faker.Commerce.ProductName(),
                Category = Faker.PickRandom("Consultation", "Laboratory", "Radiology", "Pharmacy", "Procedure"),
                DefaultUnitPrice = Faker.Random.Number(1, 500_000) / 100m,
                TaxRatePercent = Faker.PickRandom(0m, 5m, 12m, 18m),
            };
        }
    }

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
