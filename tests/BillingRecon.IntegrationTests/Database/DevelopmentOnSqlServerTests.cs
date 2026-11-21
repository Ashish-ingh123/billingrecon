using System.Net;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The Development environment on a real SQL Server: the API migrates its database at startup and adds the rows of
/// the development tenant and of the development user, so that what a developer saves satisfies the foreign keys to
/// the tenant (ADR 0015) and to the user who saves it (ADR 0030).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DevelopmentOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task The_row_of_the_development_tenant_is_added_once()
    {
        var development = new FixedIdentity(new TenantId(Guid.NewGuid()), new UserId(Guid.NewGuid()));
        var tenantId = development.TenantId!.Value;

        await using (var first = ApiContext(development))
        {
            (await DevelopmentTenantSeeder.EnsureTenantAsync(first, tenantId, Now, CancellationToken.None)).ShouldBeTrue();
        }

        await using (var second = ApiContext(development))
        {
            (await DevelopmentTenantSeeder.EnsureTenantAsync(second, tenantId, Now, CancellationToken.None)).ShouldBeFalse();
        }

        await using var system = ApiContext(SystemIdentity.Instance);
        var tenant = (await system.Set<Tenant>().ToListAsync()).ShouldHaveSingleItem();
        tenant.Id.ShouldBe(tenantId);
        tenant.Code.ShouldBe(DevelopmentTenantSeeder.TenantCode);
    }

    [Fact]
    public async Task ADR0030_the_row_of_the_development_user_is_added_once_as_an_administrator_whose_password_nothing_matches()
    {
        var development = new FixedIdentity(new TenantId(Guid.NewGuid()), new UserId(Guid.NewGuid()));
        var userId = development.UserId!.Value;
        await using (var tenantRow = ApiContext(development))
        {
            (await DevelopmentTenantSeeder.EnsureTenantAsync(tenantRow, development.TenantId!.Value, Now, CancellationToken.None)).ShouldBeTrue();
        }

        await using (var first = ApiContext(development))
        {
            (await DevelopmentTenantSeeder.EnsureUserAsync(first, userId, Now, CancellationToken.None)).ShouldBeTrue();
        }

        await using (var second = ApiContext(development))
        {
            (await DevelopmentTenantSeeder.EnsureUserAsync(second, userId, Now, CancellationToken.None)).ShouldBeFalse();
        }

        await using var reader = ApiContext(development);
        var user = await reader.Set<User>().Include(candidate => candidate.Roles).AsNoTracking().SingleAsync();
        (user.Id, user.Email, user.Status, user.CreatedByUserId).ShouldBe((userId, DevelopmentTenantSeeder.UserEmail, UserStatus.Active, (UserId?)userId));
        user.Roles.ShouldHaveSingleItem().Role.ShouldBe(Role.TenantAdmin);
        new Pbkdf2PasswordHasher().Verify(string.Empty, user.PasswordHash).ShouldBeFalse();
        user.PasswordHash.ShouldBe(Pbkdf2PasswordHasher.Unusable);
    }

    [Fact]
    public async Task In_development_the_api_migrates_a_new_database_and_adds_its_tenant_so_that_a_patient_can_be_registered()
    {
        // Development acts for the tenant and user of appsettings.Development.json; the database is a new one.
        using var factory = new DevelopmentOnSqlServerFactory(SqlServer.ConnectionStringFor("BillingRecon_Development"));
        using var client = factory.CreateClient();

        using var response = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { DateOfBirth = new DateOnly(1980, 1, 1) });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private sealed class DevelopmentOnSqlServerFactory(string connectionString) : ApiFactory
    {
        protected override string EnvironmentName => Environments.Development;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Database:ConnectionString", connectionString),
            new("Seq:ServerUrl", string.Empty),
            new("Database:ApplyMigrationsOnStartup", "true"),
        ];
    }
}
