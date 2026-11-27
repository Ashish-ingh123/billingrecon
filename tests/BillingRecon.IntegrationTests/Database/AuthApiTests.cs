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
/// in the audit trail; every refusal answers the same 401 and is logged with its reason and nothing secret. And the limit
/// per account (ADR 0035): ten refusals hold an account, real or not, for fifteen minutes, the right password included;
/// the limit per client address is tested without a database (<c>SignInRateLimitTests</c>).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuthApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Password = "correct horse battery staple";
    private const string NewPassword = "a passphrase of my very own";
    private const string Email = "Asha.Rao@Example.IN";
    private const string ColleagueEmail = "ravi.iyer@example.in";

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
        token.Claims.Where(claim => claim.Type == TokenClaims.RoleClaim).Select(claim => claim.Value).ShouldBe(["BillingClerk", "Cashier"]);

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

    [Fact]
    public async Task ADR0035_ten_refused_sign_ins_hold_the_account_for_fifteen_minutes_even_with_the_right_password()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var colleague = await administrator.PostJsonAsync(new Uri("/api/v1/users", UriKind.Relative), new CreateUserRequest { Email = ColleagueEmail, DisplayName = "Ravi Iyer", Password = Password, Roles = [Role.Cashier] });
        colleague.StatusCode.ShouldBe(HttpStatusCode.Created, await colleague.Content.ReadAsStringAsync());
        await ExecuteAsync($"UPDATE [Users] SET [PasswordChangeDue] = 0 WHERE [Id] = {(await colleague.ReadAsAsync<UserDto>()).Id}");
        using var anonymous = factory.CreateClient();

        for (var refusal = 0; refusal < SignInLimits.RefusalsPerAccount; refusal++)
        {
            using var refused = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = "a guess that is wrong" });
            await refused.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        }

        // The right password, the code and the address in another case and with spaces: the same account, held.
        using var held = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = $" {code.ToUpperInvariant()} ", Email = " ASHA.RAO@example.in ", Password = Password });

        var problem = await held.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
        problem.GetProperty("detail").GetString().ShouldBe(SignInLimits.Detail);
        held.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(SignInLimits.AccountWindow);
        held.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

        // The password was never checked, so nothing was recorded; the log names the hospital, never the address.
        await using (var context = ApiContext(admin))
        {
            (await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id))).LastSignInAtUtc.ShouldBeNull();
        }

        var warning = factory.Logs.ForCorrelationId(held.GetCorrelationId())
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("Sign-ins to tenant", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        warning.Scalar("TenantCode").ShouldBe(code);
        foreach (var json in factory.Logs.Events.Select(logEvent => logEvent.ToJson()))
        {
            json.ShouldNotContain("asha.rao", Case.Insensitive);
            json.ShouldNotContain(Password);
        }

        // Another account of the same hospital is not held.
        (await IdentityAsync(anonymous, (await SignInAsync(anonymous, code, ColleagueEmail)).AccessToken)).TenantId.ShouldBe(admin.TenantId!.Value.Value);
    }

    [Theory]
    [InlineData("nobody@example.in", null)]
    [InlineData(null, "no-such-hospital")]
    public async Task ADR0035_an_account_nobody_has_is_held_exactly_like_a_real_one_so_the_limit_tells_nothing(string? email, string? tenant)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (_, code, _) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();
        var request = new LoginRequest { Tenant = tenant ?? code, Email = email ?? Email, Password = Password };

        for (var refusal = 0; refusal < SignInLimits.RefusalsPerAccount; refusal++)
        {
            using var refused = await anonymous.PostAsJsonAsync(Login, request);
            await refused.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        }

        using var held = await anonymous.PostAsJsonAsync(Login, request);

        await held.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
        held.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(SignInLimits.AccountWindow);
    }

    [Fact]
    public async Task ADR0035_a_sign_in_that_succeeds_counts_nothing_and_only_the_tenth_refusal_holds_the_account()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (_, code, _) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();
        var wrong = new LoginRequest { Tenant = code, Email = Email, Password = "a guess that is wrong" };

        for (var refusal = 1; refusal < SignInLimits.RefusalsPerAccount; refusal++)
        {
            using var refused = await anonymous.PostAsJsonAsync(Login, wrong);
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        for (var success = 0; success < 3; success++)
        {
            await SignInAsync(anonymous, code);
        }

        using var tenth = await anonymous.PostAsJsonAsync(Login, wrong);
        using var held = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password });

        await tenth.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        await held.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
    }

    // A tenant with its administrator and a user with a password, as the administrator creates it; and the tenant's code.
    // The user an administrator created, who has already set a password of their own unless passwordChangeDue says not
    // (ADR 0051): the tests of the sign-in itself start from a user who may do anything their roles allow.
    [Fact]
    public async Task ADR0051_a_user_an_administrator_created_must_set_a_password_of_their_own_before_anything_else()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory, passwordChangeDue: true);
        using var anonymous = factory.CreateClient();

        // The first sign-in: a token of 15 minutes that only changes the password.
        var first = await SignInAsync(anonymous, code);
        (first.CredentialsChangeDue, first.ExpiresInSeconds).ShouldBe((true, 900));
        new JsonWebToken(first.AccessToken).GetClaim(TokenClaims.PasswordChangeClaim).Value.ShouldBe(TokenClaims.PasswordChangeRequired);
        using (var stopped = await SendAsync(anonymous, first.AccessToken))
        {
            await stopped.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.PasswordChangeRequiredCode);
        }

        // A wrong current password changes nothing.
        using (var wrong = await ChangePasswordAsync(anonymous, first.AccessToken, "not the password at all", NewPassword))
        {
            await wrong.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PasswordCurrentWrongCode);
        }

        // The right one: a new token of the usual hour, which acts for the user.
        using var changed = await ChangePasswordAsync(anonymous, first.AccessToken, Password, NewPassword);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        changed.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var after = await changed.ReadAsAsync<SignedInDto>();
        (after.CredentialsChangeDue, after.ExpiresInSeconds).ShouldBe((false, 3600));
        new JsonWebToken(after.AccessToken).TryGetClaim(TokenClaims.PasswordChangeClaim, out _).ShouldBeFalse();
        (await IdentityAsync(anonymous, after.AccessToken)).UserId.ShouldBe(user.Id);

        // The new password signs in with nothing due; the old one is refused.
        using (var again = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = NewPassword }))
        {
            (await again.ReadAsAsync<SignedInDto>()).CredentialsChangeDue.ShouldBeFalse();
        }

        using (var old = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password }))
        {
            await old.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.InvalidCredentialsCode);
        }

        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var record = (await TrailAsync(administrator, user.Id)).Where(entry => entry.Action == "PasswordChanged").ShouldHaveSingleItem();
        record.UserId.ShouldBe(user.Id);
        record.After.GetRawText().ShouldNotContain("pbkdf2", Case.Insensitive);
    }

    [Fact]
    public async Task ADR0051_a_reset_by_an_administrator_makes_the_change_due_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");

        using var reset = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/users/{user.Id}/password", UriKind.Relative))
        {
            Content = JsonContent.Create(new ResetUserPasswordRequest { Password = NewPassword }),
        };
        reset.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{user.Version}\""));
        using var answered = await administrator.SendAsync(reset);
        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
        (await answered.ReadAsAsync<UserDto>()).CredentialsChangeDue.ShouldBeTrue();

        using var anonymous = factory.CreateClient();
        using var signIn = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = NewPassword });
        (await signIn.ReadAsAsync<SignedInDto>()).CredentialsChangeDue.ShouldBeTrue();
    }

    [Fact]
    public async Task ADR0057_a_sign_out_refuses_every_token_of_the_user_and_nobody_elses_and_is_audited()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var anonymous = factory.CreateClient();

        // Two sign-ins of the same user, as on two devices; both act for the user.
        var desk = await SignInAsync(anonymous, code);
        var phone = await SignInAsync(anonymous, code);
        new JsonWebToken(desk.AccessToken).GetClaim(TokenClaims.SessionClaim).Value.ShouldBe("0");
        (await IdentityAsync(anonymous, desk.AccessToken)).UserId.ShouldBe(user.Id);
        (await IdentityAsync(anonymous, phone.AccessToken)).UserId.ShouldBe(user.Id);

        using (var signedOut = await LogoutAsync(anonymous, desk.AccessToken))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signedOut.Content.ReadAsStringAsync());
            signedOut.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        }

        // From the next request on, on every device, the sign-out itself included.
        using (var onTheDesk = await SendAsync(anonymous, desk.AccessToken))
        {
            await onTheDesk.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        }

        using (var onThePhone = await SendAsync(anonymous, phone.AccessToken))
        {
            await onThePhone.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        }

        using (var again = await LogoutAsync(anonymous, desk.AccessToken))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        }

        // Another user of the hospital is not concerned, and the user signs in again into a new session.
        using (var others = await administrator.GetAsync(Identity))
        {
            others.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var back = await SignInAsync(anonymous, code);
        new JsonWebToken(back.AccessToken).GetClaim(TokenClaims.SessionClaim).Value.ShouldBe("1");
        (await IdentityAsync(anonymous, back.AccessToken)).UserId.ShouldBe(user.Id);

        var record = (await TrailAsync(administrator, user.Id)).Where(entry => entry.Action == "SignedOut").ShouldHaveSingleItem();
        record.UserId.ShouldBe(user.Id);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("password reset")]
    [InlineData("roles replaced")]
    public async Task ADR0057_what_an_administrator_does_to_a_user_ends_the_users_sessions_at_once(string change)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, code, user) = await TenantWithUserAsync(factory);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var anonymous = factory.CreateClient();
        var signedIn = await SignInAsync(anonymous, code);
        (await IdentityAsync(anonymous, signedIn.AccessToken)).UserId.ShouldBe(user.Id);
        var target = $"/api/v1/users/{user.Id}/";

        // The sign-in was recorded on the user, so the administrator reads the row again for its version.
        using (var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative)))
        {
            user = await read.ReadAsAsync<UserDto>();
        }

        using var done = change switch
        {
            "disabled" => await administrator.SendWithIfMatchAsync(HttpMethod.Post, new Uri(target + "disable", UriKind.Relative), body: null, PayersApi.ETag(user.Version)),
            "password reset" => await administrator.SendWithIfMatchAsync(HttpMethod.Post, new Uri(target + "password", UriKind.Relative), new ResetUserPasswordRequest { Password = NewPassword }, PayersApi.ETag(user.Version)),
            _ => await administrator.SendWithIfMatchAsync(HttpMethod.Put, new Uri(target + "roles", UriKind.Relative), new ReplaceUserRolesRequest { Roles = [Role.Viewer] }, PayersApi.ETag(user.Version)),
        };
        done.StatusCode.ShouldBe(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());

        using var after = await SendAsync(anonymous, signedIn.AccessToken);
        await after.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);

        // The administrator who did it goes on.
        using var others = await administrator.GetAsync(Identity);
        others.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0057_a_change_of_ones_own_password_ends_the_other_sessions_and_answers_a_token_of_the_new_one()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (_, code, user) = await TenantWithUserAsync(factory);
        using var anonymous = factory.CreateClient();
        var desk = await SignInAsync(anonymous, code);
        var phone = await SignInAsync(anonymous, code);

        using var changed = await ChangePasswordAsync(anonymous, desk.AccessToken, Password, NewPassword);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var next = await changed.ReadAsAsync<SignedInDto>();

        (await IdentityAsync(anonymous, next.AccessToken)).UserId.ShouldBe(user.Id);
        using var onTheDesk = await SendAsync(anonymous, desk.AccessToken);
        await onTheDesk.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        using var onThePhone = await SendAsync(anonymous, phone.AccessToken);
        await onThePhone.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
    }

    [Fact]
    public async Task ADR0057_a_token_whose_user_is_of_another_hospital_or_does_not_exist_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, _, user) = await TenantWithUserAsync(factory);
        var other = await NewApiTenantAsync();
        using var anonymous = factory.CreateClient();

        // Signed by this API, but naming a user the tenant of the token does not have.
        using var foreign = await SendAsync(anonymous, factory.TokenFor(other.TenantId!.Value, new UserId(user.Id), "TenantAdmin"));
        using var nobody = await SendAsync(anonymous, factory.TokenFor(admin.TenantId!.Value, new UserId(Guid.NewGuid()), "TenantAdmin"));

        await foreign.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        await nobody.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/logout", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string token, string current, string chosen)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/password", UriKind.Relative))
        {
            Content = JsonContent.Create(new ChangePasswordRequest { CurrentPassword = current, NewPassword = chosen }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }

    private async Task<(FixedIdentity Admin, string Code, UserDto User)> TenantWithUserAsync(DatabaseApiFactory factory, bool passwordChangeDue = false)
    {
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");

        using var created = await administrator.PostJsonAsync(new Uri("/api/v1/users", UriKind.Relative), new CreateUserRequest { Email = Email, DisplayName = "Asha Rao", Password = Password, Roles = [Role.Cashier, Role.BillingClerk] });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        await using var context = ApiContext(admin);
        var code = (await context.Set<Tenant>().AsNoTracking().SingleAsync()).Code;
        var user = await created.ReadAsAsync<UserDto>();
        user.CredentialsChangeDue.ShouldBeTrue("a user an administrator creates changes the password at the first sign-in");
        if (passwordChangeDue)
        {
            return (admin, code, user);
        }

        await ExecuteAsync($"UPDATE [Users] SET [PasswordChangeDue] = 0 WHERE [Id] = {user.Id}");
        using var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative));

        return (admin, code, await read.ReadAsAsync<UserDto>());
    }

    private static async Task<SignedInDto> SignInAsync(HttpClient client, string code, string email = Email)
    {
        using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = email, Password = Password });

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
