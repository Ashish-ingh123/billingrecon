using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Onboarding a hospital and suspending it by the operator's command (ADR 0052), on a migrated database as the
/// application's login, through the API's own services: the tenant with its settings and prefixes and its first
/// TenantAdmin, who signs in with the one-time password and must change it (ADR 0051); a second run with the same code
/// refused; a suspended hospital's users refused at sign-in and with a token issued before, and nothing of it deleted;
/// every step audited with no user.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class TenantOnboardingOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Code = "city-hospital";
    private const string AdminEmail = "admin@city.example";
    private const string ChosenPassword = "a passphrase of my very own";

    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    private static OnboardTenant City => new()
    {
        Code = Code,
        Name = "City Hospital",
        AdminEmail = AdminEmail,
        AdminDisplayName = "A. Rao",
        PostingApprovalThreshold = 100_000m,
        MaxBackdateDays = 3,
        DefaultDueDays = 15,
        Prefixes = new Dictionary<DocumentType, string> { [DocumentType.Bill] = "INV", [DocumentType.CreditNote] = "CRN" },
    };

    [Fact]
    public async Task ADR0052_the_command_onboards_a_hospital_whose_administrator_signs_in_and_must_change_the_password()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();

        var onboarded = (await OnboardAsync(factory, City)).Value;

        onboarded.OneTimePassword.Length.ShouldBe(TenantOperations.OneTimePasswordLength);
        onboarded.ToString().ShouldNotContain(onboarded.OneTimePassword);
        await using (var context = ApiContext(new FixedIdentity(onboarded.TenantId)))
        {
            var tenant = await context.Set<Tenant>().SingleAsync();
            (tenant.Code, tenant.Name, tenant.Status).ShouldBe((Code, "City Hospital", TenantStatus.Active));
            (tenant.PostingApprovalThreshold, tenant.MaxBackdateDays, tenant.DefaultDueDays).ShouldBe((Money.Of(100_000m), 3, 15));
            tenant.Prefixes.Values.ShouldBe(["INV", "CRN", "PAY", "ADJ", "REF", "OBR", "OBA"]);

            var admin = await context.Set<User>().Include(user => user.Roles).SingleAsync();
            (admin.Id, admin.Email, admin.PasswordChangeDue).ShouldBe((onboarded.AdminUserId, AdminEmail, true));
            admin.Roles.Select(role => role.Role).ShouldBe([Role.TenantAdmin]);

            var trail = await context.Set<AuditRecord>().AsNoTracking().OrderBy(record => record.Id).ToListAsync();
            trail.ShouldAllBe(record => record.UserId == null);
            trail.Select(record => (record.EntityType, record.Action)).ShouldBe(
            [
                ("Tenant", "Onboarded"),
                ("Tenant", "SettingsChanged"),
                ("Tenant", "PrefixChanged"),
                ("Tenant", "PrefixChanged"),
                ("User", "Created"),
            ], ignoreOrder: true);
            trail.Select(record => record.AfterJson).ShouldAllBe(json => !json.Contains(onboarded.OneTimePassword, StringComparison.Ordinal));
        }

        using var signIn = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = Code, Email = AdminEmail, Password = onboarded.OneTimePassword });
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
        var signedIn = await signIn.ReadAsAsync<SignedInDto>();
        signedIn.CredentialsChangeDue.ShouldBeTrue();

        using var users = await GetAsync(client, "/api/v1/users", signedIn.AccessToken);
        await users.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.PasswordChangeRequiredCode);

        using var change = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/password", UriKind.Relative))
        {
            Content = JsonContent.Create(new ChangePasswordRequest { CurrentPassword = onboarded.OneTimePassword, NewPassword = ChosenPassword }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);
        using var changed = await client.SendAsync(change);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        using var after = await GetAsync(client, "/api/v1/users", (await changed.ReadAsAsync<SignedInDto>()).AccessToken);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0052_a_second_run_with_the_same_code_is_refused_and_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var first = (await OnboardAsync(factory, City)).Value;

        var second = await OnboardAsync(factory, City with { Name = "Another", AdminEmail = "other@city.example" });

        second.Error.Code.ShouldBe(IdentityErrors.TenantCodeTakenCode);
        await using var context = ApiContext(new FixedIdentity(first.TenantId));
        (await context.Set<Tenant>().SingleAsync()).Name.ShouldBe("City Hospital");
        (await context.Set<User>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ADR0052_what_the_command_is_given_is_checked_before_anything_is_written()
    {
        using var factory = new DatabaseApiFactory(SqlServer);

        var refused = await OnboardAsync(factory, City with
        {
            Code = "City Hospital",
            AdminEmail = "nobody",
            MaxBackdateDays = 400,
            Prefixes = new Dictionary<DocumentType, string> { [DocumentType.Bill] = "PAY" },
        });

        refused.Error.Code.ShouldBe(IdentityErrors.TenantInvalidCode);
        refused.Error.FieldErrors.Keys.ShouldBe(["code", "adminEmail", "backdateDays", "prefixes"], ignoreOrder: true);
        await using var system = ApiContext(SystemIdentity.Instance);
        (await system.Set<Tenant>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ADR0052_two_types_may_swap_their_standard_prefixes()
    {
        using var factory = new DatabaseApiFactory(SqlServer);

        var onboarded = (await OnboardAsync(factory, City with
        {
            Prefixes = new Dictionary<DocumentType, string> { [DocumentType.Payment] = "REF", [DocumentType.Refund] = "PAY" },
        })).Value;

        await using var context = ApiContext(new FixedIdentity(onboarded.TenantId));
        var tenant = await context.Set<Tenant>().SingleAsync();
        (tenant.PrefixOf(DocumentType.Payment), tenant.PrefixOf(DocumentType.Refund)).ShouldBe(("REF", "PAY"));
    }

    [Fact]
    public async Task ADR0052_a_suspended_hospital_is_refused_at_sign_in_and_with_a_token_issued_before_and_nothing_of_it_is_deleted()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();
        var onboarded = (await OnboardAsync(factory, City)).Value;
        var token = await ChangedPasswordTokenAsync(client, onboarded.OneTimePassword);
        (await GetAsync(client, "/api/v1/users", token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ITenantOperations>().SuspendAsync(Code, "Contract ended", default)).IsSuccess.ShouldBeTrue();
            (await scope.ServiceProvider.GetRequiredService<ITenantOperations>().SuspendAsync(Code, "Again", default)).Error.Code
                .ShouldBe(IdentityErrors.TenantStatusUnchangedCode);
        }

        // The status read before is kept at most 30 seconds.
        factory.Clock.Advance(TenantStatusCache.Lifetime);
        using (var refused = await GetAsync(client, "/api/v1/users", token))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.TenantSuspendedCode);
        }

        using (var signIn = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = Code, Email = AdminEmail, Password = ChosenPassword }))
        {
            await signIn.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        }

        await using (var context = ApiContext(new FixedIdentity(onboarded.TenantId)))
        {
            (await context.Set<Tenant>().SingleAsync()).Status.ShouldBe(TenantStatus.Suspended);
            (await context.Set<User>().CountAsync()).ShouldBe(1);
            var suspended = await context.Set<AuditRecord>().SingleAsync(record => record.Action == "Suspended");
            (suspended.Reason, suspended.UserId).ShouldBe(("Contract ended", (UserId?)null));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ITenantOperations>().ResumeAsync(Code, "Contract renewed", default)).IsSuccess.ShouldBeTrue();
        }

        factory.Clock.Advance(TenantStatusCache.Lifetime);
        (await GetAsync(client, "/api/v1/users", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0052_suspending_a_code_no_hospital_has_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        await using var scope = factory.Services.CreateAsyncScope();

        var refused = await scope.ServiceProvider.GetRequiredService<ITenantOperations>().SuspendAsync("no-such", "A reason", default);

        refused.Error.Code.ShouldBe(IdentityErrors.TenantNotFoundCode);
    }

    private static async Task<Domain.Common.Result<OnboardedTenant>> OnboardAsync(DatabaseApiFactory factory, OnboardTenant command)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantOperations>().OnboardAsync(command, default);
    }

    private static async Task<string> ChangedPasswordTokenAsync(HttpClient client, string oneTimePassword)
    {
        using var signIn = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = Code, Email = AdminEmail, Password = oneTimePassword });
        var first = await signIn.ReadAsAsync<SignedInDto>();
        using var change = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/password", UriKind.Relative))
        {
            Content = JsonContent.Create(new ChangePasswordRequest { CurrentPassword = oneTimePassword, NewPassword = ChosenPassword }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        using var changed = await client.SendAsync(change);

        return (await changed.ReadAsAsync<SignedInDto>()).AccessToken;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }
}
