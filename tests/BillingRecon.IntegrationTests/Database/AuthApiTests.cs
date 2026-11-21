using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Signing in end to end, on the pipeline that ships and a real SQL Server (US-02; BR-601, BR-602; ADR 0031): a user
/// signs in with the code of the hospital, the address and the password, in any case; the token carries the tenant,
/// the user and the roles and is the request's only identity until it expires; the sign-in is recorded on the user and
/// in the audit trail; every refusal answers the same 401 and is logged with its reason and nothing secret.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuthApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Password = "correct horse battery staple";
    private const string Email = "Asha.Rao@Example.IN";

    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Identity = new("/api/v1/probe/identity", UriKind.Relative);

    [Fact]
    public async Task US02_a_user_signs_in_with_the_code_the_address_and_the_password_and_the_token_acts_for_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();

        // The code and the address in another case, with spaces around them.
        using var response = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = $" {code.ToUpperInvariant()} ", Email = " asha.rao@EXAMPLE.in ", Password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("password", Case.Insensitive);
        body.ShouldNotContain("pbkdf2", Case.Insensitive);
        var signedIn = await response.ReadAsAsync<SignedInDto>();
        (signedIn.TokenType, signedIn.ExpiresAtUtc, signedIn.ExpiresInSeconds).ShouldBe(("Bearer", DatabaseApiFactory.InitialInstant.AddMinutes(60), 3600));

        // BR-602: the token carries the tenant, the user and the roles.
        var token = new JsonWebToken(signedIn.AccessToken);
        token.Subject.ShouldBe(user.Id.ToString());
        token.GetClaim(TokenClaims.TenantIdClaim).Value.ShouldBe(admin.TenantId.ToString());
        token.Claims.Where(claim => claim.Type == TokenClaims.RoleClaim).Select(claim => claim.Value).ShouldBe(["Supervisor", "Cashier"]);

        var acting = await IdentityAsync(anonymous, signedIn.AccessToken);
        (acting.TenantId, acting.UserId).ShouldBe((admin.TenantId!.Value.Value, user.Id));
    }

    [Fact]
    public async Task US02_a_sign_in_is_recorded_on_the_user_and_audited_as_the_user_and_changes_its_version()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var anonymous = factory.CreateClient();
        factory.Clock.Advance(TimeSpan.FromMinutes(5));

        await SignInAsync(anonymous, code);

        using var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative));
        var after = await read.ReadAsAsync<UserDto>();
        after.LastSignInAtUtc.ShouldBe(factory.Clock.UtcNow);
        after.Version.ShouldNotBe(user.Version, "signing in writes the user's row");

        var signedIn = (await TrailAsync(administrator, user.Id)).Where(record => record.Action == "SignedIn").ShouldHaveSingleItem();
        (signedIn.UserId, signedIn.OccurredAtUtc).ShouldBe(((Guid?)user.Id, factory.Clock.UtcNow), "the sign-in is the user's own act");
        signedIn.After.GetRawText().ShouldNotContain("pbkdf2", Case.Insensitive);

        // An administrator who read the user before the sign-in reads it again before a change (ADR 0031, point 5).
        using var stale = await administrator.SendWithIfMatchAsync(HttpMethod.Patch, new Uri($"/api/v1/users/{user.Id}", UriKind.Relative), new UpdateUserRequest { Email = Email, DisplayName = "Dr Asha Rao" }, PayersApi.ETag(user.Version));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
    }

    [Theory]
    [InlineData(SignInRefusal.UnknownTenant)]
    [InlineData(SignInRefusal.TenantSuspended)]
    [InlineData(SignInRefusal.UnknownUser)]
    [InlineData(SignInRefusal.WrongPassword)]
    [InlineData(SignInRefusal.UserDisabled)]
    public async Task BR601_every_refused_sign_in_answers_the_same_401_and_logs_why_without_the_address_or_the_password(SignInRefusal refusal)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();
        var request = new LoginRequest { Tenant = code, Email = Email, Password = Password };
        switch (refusal)
        {
            case SignInRefusal.UnknownTenant:
                request = request with { Tenant = "no-such-hospital" };
                break;
            case SignInRefusal.TenantSuspended:
                await ExecuteAsync($"UPDATE dbo.Tenants SET [Status] = 'Suspended' WHERE [Id] = {admin.TenantId!.Value.Value}");
                break;
            case SignInRefusal.UnknownUser:
                request = request with { Email = "nobody@example.in" };
                break;
            case SignInRefusal.WrongPassword:
                request = request with { Password = "correct horse battery stapler" };
                break;
            case SignInRefusal.UserDisabled:
                await DisableAsync(admin, user.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a refusal.");
        }

        using var response = await anonymous.PostAsJsonAsync(Login, request);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        problem.GetProperty("detail").GetString().ShouldBe(IdentityErrors.InvalidCredentials().Message);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

        // The reason is in the log, with the tenant's code and the user when the address was found; nothing secret is.
        var refused = factory.Logs.ForCorrelationId(response.GetCorrelationId())
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("A sign-in to tenant", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        refused.Level.ShouldBe(Serilog.Events.LogEventLevel.Warning);
        refused.Scalar("Refusal").ShouldNotBeNull().ToString().ShouldBe(refusal.ToString());
        refused.Scalar("UserId")?.ToString().ShouldBe(refusal is SignInRefusal.WrongPassword or SignInRefusal.UserDisabled ? user.Id.ToString() : null);
        if (refusal is SignInRefusal.WrongPassword or SignInRefusal.UserDisabled)
        {
            refused.Scalar("UserId").ShouldNotBeNull("the address was found, so the log says whose it is");
        }
        foreach (var json in factory.Logs.Events.Select(logEvent => logEvent.ToJson()))
        {
            json.ShouldNotContain("correct horse battery");
            json.ShouldNotContain("asha.rao", Case.Insensitive);
        }

        await using var context = ApiContext(admin);
        (await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id))).LastSignInAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task US02_a_sign_in_ignores_any_token_it_carries_and_signs_in_to_the_tenant_it_names()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        var other = await NewApiTenantAsync();
        using var signedInElsewhere = factory.CreateClientFor(other, "TenantAdmin");

        using var response = await signedInElsewhere.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var token = new JsonWebToken((await response.ReadAsAsync<SignedInDto>()).AccessToken);
        (token.Subject, token.GetClaim(TokenClaims.TenantIdClaim).Value).ShouldBe((user.Id.ToString(), admin.TenantId.ToString()));
    }

    [Fact]
    public async Task ADR0031_a_token_expires_after_60_minutes_and_the_user_signs_in_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (_, code, user) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();
        var first = await SignInAsync(anonymous, code);

        factory.Clock.Advance(TimeSpan.FromMinutes(62));

        using var expired = await SendAsync(anonymous, first.AccessToken);
        await expired.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        var second = await SignInAsync(anonymous, code);
        (await IdentityAsync(anonymous, second.AccessToken)).UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task A_sign_in_of_the_wrong_shape_is_a_validation_error_that_names_the_field_and_nothing_is_logged_as_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (_, code, _) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();

        using var noPassword = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email });
        using var unknownMember = await anonymous.PostAsync(Login, PayersApi.RawJson($$"""{"tenant":"{{code}}","email":"{{Email}}","password":"{{Password}}","rememberMe":true}"""));

        var problem = await noPassword.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["password"], Case.Insensitive);
        await unknownMember.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        factory.Logs.Events.ShouldNotContain(logEvent => logEvent.MessageTemplate.Text.StartsWith("A sign-in to tenant", StringComparison.Ordinal));
    }

    // A tenant with its administrator and a user with a password, as the administrator creates it; and the tenant's code.
    private async Task<(FixedIdentity Admin, string Code, UserDto User)> TenantWithUserAsync(DatabaseApiFactory factory)
    {
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");

        using var created = await administrator.PostJsonAsync(new Uri("/api/v1/users", UriKind.Relative), new CreateUserRequest { Email = Email, DisplayName = "Asha Rao", Password = Password, Roles = [Role.Cashier, Role.Supervisor] });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        await using var context = ApiContext(admin);
        var code = (await context.Set<Tenant>().AsNoTracking().SingleAsync()).Code;

        return (admin, code, await created.ReadAsAsync<UserDto>());
    }

    private static async Task<SignedInDto> SignInAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<SignedInDto>();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Identity);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }

    private static async Task<IdentityResponse> IdentityAsync(HttpClient client, string token)
    {
        using var response = await SendAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IdentityResponse>()).ShouldNotBeNull();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=User&entityId={id}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    private async Task DisableAsync(FixedIdentity admin, Guid userId)
    {
        await using var context = ApiContext(admin);
        var row = await context.Set<User>().SingleAsync(candidate => candidate.Id == new UserId(userId));
        row.Disable().IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync();
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        (await context.Database.ExecuteSqlAsync(sql)).ShouldBe(1);
    }
}
