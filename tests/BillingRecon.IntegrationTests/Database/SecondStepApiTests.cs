using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The second step of a sign-in end to end, on the pipeline that ships and a real SQL Server (ADR 0061): enrolment
/// with a secret that is unreadable at rest, a code good for its own step and the two beside it and good once,
/// recovery codes good once, the token between the two steps that reaches nothing else, a decision that needs the
/// second step, an administrator's reset that ends the user's sessions and holds their decisions for 24 hours, and
/// the fifth wrong code that ends the sign-in.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SecondStepApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Email = "meera@cityhospital.org";
    private const string Password = "a long passphrase of mine";
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Users = new("/api/v1/users", UriKind.Relative);

    // A decision: the approval of a prefix change that does not exist. Past the gates it is refused for its missing
    // If-Match (428); a gate that stops it answers 403.
    private static readonly Uri Decision = new($"/api/v1/settings/prefix-changes/{Guid.NewGuid()}/approve", UriKind.Relative);

    [Fact]
    public async Task ADR0061_a_supervisor_enrols_at_the_first_sign_in_and_gives_a_code_at_every_later_one()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Supervisor);

        // The password alone answers a token that only enrols: 15 minutes, and 403 everywhere else.
        var first = await SignInAsync(anonymous, code);
        (first.SecondStep, first.ExpiresInSeconds, first.CredentialsChangeDue).ShouldBe((SecondStepStage.Enrolment, 900, false));
        using var early = await anonymous.PostWithTokenAsync(Decision, first.AccessToken);
        await early.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepEnrolmentRequiredCode);
        using var noSecondStepYet = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, first.AccessToken, new SecondStepCodeRequest { Code = "123456" });
        await noSecondStepYet.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepEnrolmentRequiredCode);

        // Begin: the secret, as text and as the address of a QR code. A wrong first code confirms nothing.
        var begun = await anonymous.BeginEnrolmentAsync(first.AccessToken);
        begun.Secret.Length.ShouldBe(32);
        begun.ProvisioningUri.ShouldBe($"otpauth://totp/BillingRecon:meera%40cityhospital.org?secret={begun.Secret}&issuer=BillingRecon&algorithm=SHA1&digits=6&period=30");
        begun.ToString().ShouldNotContain(begun.Secret);
        using var wrong = await anonymous.PostWithTokenAsync(SecondStepApi.Confirmation, first.AccessToken, new SecondStepCodeRequest { Code = Other(SecondStepApi.CodeAt(begun.Secret, factory.Clock.UtcNow)) });
        await wrong.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepCodeWrongCode);

        // The right code: ten recovery codes, shown this once, and a full session whose second step is given.
        using var confirmed = await anonymous.PostWithTokenAsync(SecondStepApi.Confirmation, first.AccessToken, new SecondStepCodeRequest { Code = SecondStepApi.CodeAt(begun.Secret, factory.Clock.UtcNow) });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        confirmed.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var enrolled = await confirmed.ReadAsAsync<SecondStepEnrolledDto>();
        enrolled.RecoveryCodes.Count.ShouldBe(10);
        enrolled.RecoveryCodes.Distinct().Count().ShouldBe(10);
        enrolled.RecoveryCodes.ShouldAllBe(recovery => recovery.Length == 19 && recovery.Count(character => character == '-') == 3);
        enrolled.ToString().ShouldNotContain(enrolled.RecoveryCodes[0]);
        (enrolled.SignedIn.SecondStep, enrolled.SignedIn.ExpiresInSeconds).ShouldBe((SecondStepStage.Done, 3600));
        using var decides = await anonymous.PostWithTokenAsync(Decision, enrolled.SignedIn.AccessToken);
        decides.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // The enrolment ended every earlier session, the one that enrolled among them (ADR 0057).
        using var old = await anonymous.PostWithTokenAsync(SecondStepApi.Enrolment, first.AccessToken);
        await old.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);

        // At rest: the secret is not readable, and neither it nor a recovery code is in any column or audit image.
        await using (var context = ApiContext(admin))
        {
            var row = await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id));
            row.SecondStepSecret.ShouldNotBeNull().ShouldStartWith("v1.");
            row.SecondStepSecret.ShouldNotContain(begun.Secret);
            row.SecondStepSecret.ShouldNotContain(Convert.ToBase64String(SecondStepApi.FromBase32(begun.Secret)));
            row.SecondStepPendingSecret.ShouldBeNull();
            row.RecoveryCodeHashes.ShouldNotBeNull().Split(' ').Length.ShouldBe(10);
            enrolled.RecoveryCodes.ShouldAllBe(recovery => !row.RecoveryCodeHashes.Contains(recovery, StringComparison.OrdinalIgnoreCase));
        }

        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var trail = await TrailAsync(administrator, user.Id);
        trail.Select(record => record.Action).ShouldBe(["Created", "SignedIn", "SecondStepEnrolmentBegun", "SecondStepEnrolled"], ignoreOrder: true);
        trail.ShouldAllBe(record => !record.After.GetRawText().Contains("SecondStepSecret", StringComparison.Ordinal)
            && !record.After.GetRawText().Contains("RecoveryCodeHashes", StringComparison.Ordinal)
            && !record.After.GetRawText().Contains(begun.Secret, StringComparison.Ordinal));
        using var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative));
        (await read.ReadAsAsync<UserDto>()).SecondStepEnrolled.ShouldBeTrue();

        // From now on: the password answers a token that only gives the code, for 5 minutes.
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await SignInAsync(anonymous, code);
        (second.SecondStep, second.ExpiresInSeconds).ShouldBe((SecondStepStage.Pending, 300));
        using var blocked = await anonymous.PostWithTokenAsync(Decision, second.AccessToken);
        await blocked.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepRequiredCode);

        // The password alone never replaces the second step there is.
        using var reEnrol = await anonymous.PostWithTokenAsync(SecondStepApi.Enrolment, second.AccessToken);
        await reEnrol.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepRequiredCode);

        using var given = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, second.AccessToken, new SecondStepCodeRequest { Code = SecondStepApi.CodeAt(begun.Secret, factory.Clock.UtcNow) });
        given.StatusCode.ShouldBe(HttpStatusCode.OK, await given.Content.ReadAsStringAsync());
        var full = await given.ReadAsAsync<SignedInDto>();
        (full.SecondStep, full.ExpiresInSeconds).ShouldBe((SecondStepStage.Done, 3600));
        using var after = await anonymous.PostWithTokenAsync(Decision, full.AccessToken);
        after.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // A second step is enrolled once: an administrator takes it away before another is set up.
        using var again = await anonymous.PostWithTokenAsync(SecondStepApi.Enrolment, full.AccessToken);
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepAlreadyEnrolledCode);
    }

    [Fact]
    public async Task RFC6238_a_code_is_good_for_its_step_and_the_two_beside_it_and_only_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (_, code, _) = await TenantWithUserAsync(factory, Role.Supervisor);
        var (secret, _) = await anonymous.EnrolAsync((await SignInAsync(anonymous, code)).AccessToken, factory.Clock.UtcNow);

        // Some minutes on, so that the steps below are all after the one the enrolment spent.
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        async Task<HttpStatusCode> GiveAsync(int stepsAway)
        {
            var pending = await SignInAsync(anonymous, code);
            using var response = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, pending.AccessToken, new SecondStepCodeRequest { Code = SecondStepApi.CodeAt(secret, factory.Clock.UtcNow, stepsAway) });

            return response.StatusCode;
        }

        // Two steps away, either way: refused. The step before: accepted (a phone's clock that runs a little slow).
        (await GiveAsync(-2)).ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GiveAsync(2)).ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GiveAsync(-1)).ShouldBe(HttpStatusCode.OK);

        // The same code again: refused. A code is good once, and so is every step before the one accepted.
        (await GiveAsync(-1)).ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GiveAsync(0)).ShouldBe(HttpStatusCode.OK);
        (await GiveAsync(0)).ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GiveAsync(-1)).ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The step after: accepted (a clock that runs a little fast), and then spent too.
        (await GiveAsync(1)).ShouldBe(HttpStatusCode.OK);
        (await GiveAsync(1)).ShouldBe(HttpStatusCode.UnprocessableEntity);

        // What is no code at all is refused the same way.
        foreach (var nonsense in new[] { string.Empty, "12345", "1234567", "abcdef", "12 34 56" })
        {
            var pending = await SignInAsync(anonymous, code);
            using var response = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, pending.AccessToken, new SecondStepCodeRequest { Code = nonsense });
            await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepCodeWrongCode);
        }
    }

    [Fact]
    public async Task ADR0061_a_recovery_code_signs_in_once_however_it_is_typed()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Supervisor);
        var (_, enrolled) = await anonymous.EnrolAsync((await SignInAsync(anonymous, code)).AccessToken, factory.Clock.UtcNow);
        var recovery = enrolled.RecoveryCodes[3];

        async Task<HttpResponseMessage> GiveAsync(string given)
            => await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, (await SignInAsync(anonymous, code)).AccessToken, new SecondStepCodeRequest { Code = given });

        // In small letters and without its dashes, as a person types it.
        using var first = await GiveAsync($" {recovery.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()} ");
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        (await first.ReadAsAsync<SignedInDto>()).SecondStep.ShouldBe(SecondStepStage.Done);

        using var twice = await GiveAsync(recovery);
        await twice.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepCodeWrongCode);
        using var neverIssued = await GiveAsync("AAAA-BBBB-CCCC-DDDD");
        await neverIssued.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepCodeWrongCode);
        using var another = await GiveAsync(enrolled.RecoveryCodes[0]);
        another.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var context = ApiContext(admin);
        (await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id)))
            .RecoveryCodeHashes.ShouldNotBeNull().Split(' ').Length.ShouldBe(8);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        (await TrailAsync(administrator, user.Id)).Count(record => record.Action == "RecoveryCodeUsed").ShouldBe(2);
    }

    [Fact]
    public async Task ADR0061_a_checker_whose_sign_in_gave_no_second_step_decides_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (admin, _, user) = await TenantWithUserAsync(factory, Role.Supervisor);
        var issuer = factory.Services.GetRequiredService<ITokenIssuer>();
        string TokenAt(SecondStepStage stage) => issuer.Issue(admin.TenantId!.Value, new UserId(user.Id), [Role.Supervisor], passwordChangeDue: false, sessionVersion: 0, stage).Value;

        // A token of the checker's role without the mark of a second step, however it came to be: no decision.
        using var without = await anonymous.PostWithTokenAsync(Decision, TokenAt(SecondStepStage.None));
        await without.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepRequiredCode);
        using var with = await anonymous.PostWithTokenAsync(Decision, TokenAt(SecondStepStage.Done));
        with.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // Reading the inbox is no decision, and neither is anything else the role may do.
        using var inbox = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/approval-requests", UriKind.Relative));
        inbox.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenAt(SecondStepStage.None));
        using var read = await anonymous.SendAsync(inbox);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The token between the two steps reaches the sign-out and nothing else.
        using var users = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/approval-requests", UriKind.Relative));
        users.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenAt(SecondStepStage.Pending));
        using var refused = await anonymous.SendAsync(users);
        await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.SecondStepRequiredCode);
        using var signOut = await anonymous.PostWithTokenAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), TokenAt(SecondStepStage.Pending));
        signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ADR0061_a_cashier_needs_no_second_step_and_may_set_one_up()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (_, code, _) = await TenantWithUserAsync(factory, Role.Cashier);

        var plain = await SignInAsync(anonymous, code);
        (plain.SecondStep, plain.ExpiresInSeconds).ShouldBe((SecondStepStage.None, 3600));

        var (secret, enrolled) = await anonymous.EnrolAsync(plain.AccessToken, factory.Clock.UtcNow);
        enrolled.SignedIn.SecondStep.ShouldBe(SecondStepStage.Done);

        // Once set up it is asked for, whatever the role.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var next = await SignInAsync(anonymous, code);
        next.SecondStep.ShouldBe(SecondStepStage.Pending);
        using var given = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, next.AccessToken, new SecondStepCodeRequest { Code = SecondStepApi.CodeAt(secret, factory.Clock.UtcNow) });
        given.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0061_the_fifth_wrong_code_in_a_row_ends_the_sign_in()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Supervisor);
        var (secret, _) = await anonymous.EnrolAsync((await SignInAsync(anonymous, code)).AccessToken, factory.Clock.UtcNow);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var pending = await SignInAsync(anonymous, code);
        var right = SecondStepApi.CodeAt(secret, factory.Clock.UtcNow);

        for (var attempt = 1; attempt <= User.SecondStepFailuresAllowed; attempt++)
        {
            using var wrong = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, pending.AccessToken, new SecondStepCodeRequest { Code = Other(right) });
            await wrong.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.SecondStepCodeWrongCode);
        }

        // The token of that sign-in is over, the right code with it; the user signs in again with the password.
        using var late = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, pending.AccessToken, new SecondStepCodeRequest { Code = right });
        await late.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        var fresh = await SignInAsync(anonymous, code);
        using var given = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, fresh.AccessToken, new SecondStepCodeRequest { Code = right });
        given.StatusCode.ShouldBe(HttpStatusCode.OK, await given.Content.ReadAsStringAsync());

        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var failures = (await TrailAsync(administrator, user.Id)).Where(record => record.Action == "SecondStepFailed").ToList();
        failures.Count.ShouldBe(User.SecondStepFailuresAllowed);
    }

    [Fact]
    public async Task ADR0061_an_administrators_reset_ends_the_users_sessions_and_holds_their_decisions_for_24_hours()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Supervisor);
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var (_, enrolled) = await anonymous.EnrolAsync((await SignInAsync(anonymous, code)).AccessToken, factory.Clock.UtcNow);
        using var before = await anonymous.PostWithTokenAsync(Decision, enrolled.SignedIn.AccessToken);
        before.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // The administrator takes the second step away: with the version read, audited as theirs.
        using var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative));
        var current = await read.ReadAsAsync<UserDto>();
        using var unversioned = await administrator.PostJsonAsync(new Uri($"/api/v1/users/{user.Id}/second-step/reset", UriKind.Relative), new { });
        unversioned.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        using var reset = await administrator.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/users/{user.Id}/second-step/reset", UriKind.Relative), body: null, PayersApi.ETag(current.Version));
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        var after = await reset.ReadAsAsync<UserDto>();
        (after.SecondStepEnrolled, after.ApprovalHoldUntilUtc).ShouldBe((false, factory.Clock.UtcNow + User.ApprovalHold));
        var record = (await TrailAsync(administrator, user.Id)).Single(candidate => candidate.Action == "SecondStepReset");
        record.UserId.ShouldBe(admin.UserId!.Value.Value);

        // Every session of the user ended; at the next sign-in they enrol again, with a new secret.
        using var ended = await anonymous.PostWithTokenAsync(Decision, enrolled.SignedIn.AccessToken);
        await ended.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        var again = await SignInAsync(anonymous, code);
        again.SecondStep.ShouldBe(SecondStepStage.Enrolment);
        var (_, reEnrolled) = await anonymous.EnrolAsync(again.AccessToken, factory.Clock.UtcNow);

        // For 24 hours the user decides nothing, second step or not: who reset it could have enrolled as them (R5).
        using var held = await anonymous.PostWithTokenAsync(Decision, reEnrolled.SignedIn.AccessToken);
        var problem = await held.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.ApprovalOnHoldCode);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("2026-10-18");
        using var inbox = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/approval-requests", UriKind.Relative));
        inbox.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reEnrolled.SignedIn.AccessToken);
        using var reads = await anonymous.SendAsync(inbox);
        reads.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A minute before the day is over: still held. After it: decisions again (a new sign-in, the token having expired).
        factory.Clock.Advance(User.ApprovalHold - TimeSpan.FromMinutes(1));
        using var stillHeld = await anonymous.PostWithTokenAsync(Decision, factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(admin.TenantId!.Value, new UserId(user.Id), [Role.Supervisor], passwordChangeDue: false, sessionVersion: await SessionVersionAsync(admin, user.Id), SecondStepStage.Done).Value);
        await stillHeld.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.ApprovalOnHoldCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        using var free = await anonymous.PostWithTokenAsync(Decision, factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(admin.TenantId!.Value, new UserId(user.Id), [Role.Supervisor], passwordChangeDue: false, sessionVersion: await SessionVersionAsync(admin, user.Id), SecondStepStage.Done).Value);
        free.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task ADR0061_a_password_an_administrator_resets_holds_the_users_decisions_too_and_another_hospital_resets_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (admin, _, user) = await TenantWithUserAsync(factory, Role.Supervisor);
        var elsewhere = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var outsider = factory.CreateClientFor(elsewhere, "TenantAdmin");
        using var cashier = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Cashier");

        using var read = await administrator.GetAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative));
        var current = await read.ReadAsAsync<UserDto>();
        current.ApprovalHoldUntilUtc.ShouldBeNull();

        // BR-002: the user of another hospital is not found; a cashier is no administrator.
        var resetUri = new Uri($"/api/v1/users/{user.Id}/second-step/reset", UriKind.Relative);
        using var across = await outsider.SendWithIfMatchAsync(HttpMethod.Post, resetUri, body: null, PayersApi.ETag(current.Version));
        await across.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.UserNotFoundCode);
        using var notAllowed = await cashier.SendWithIfMatchAsync(HttpMethod.Post, resetUri, body: null, PayersApi.ETag(current.Version));
        notAllowed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var reset = await administrator.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/users/{user.Id}/password", UriKind.Relative), new ResetUserPasswordRequest { Password = "another long passphrase" }, PayersApi.ETag(current.Version));
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        (await reset.ReadAsAsync<UserDto>()).ApprovalHoldUntilUtc.ShouldBe(factory.Clock.UtcNow + User.ApprovalHold);

        using var anonymous = factory.CreateClient();
        using var held = await anonymous.PostWithTokenAsync(Decision, factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(admin.TenantId!.Value, new UserId(user.Id), [Role.Supervisor], passwordChangeDue: false, sessionVersion: await SessionVersionAsync(admin, user.Id), SecondStepStage.Done).Value);
        await held.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.ApprovalOnHoldCode);
    }

    // A code that is certainly not the right one: its last digit moved on by one.
    private static string Other(string code) => code[..^1] + (char)('0' + (((code[^1] - '0') + 1) % 10));

    private static async Task<SignedInDto> SignInAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<SignedInDto>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=User&entityId={id}&pageSize=100", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    private async Task<int> SessionVersionAsync(FixedIdentity admin, Guid userId)
    {
        await using var context = ApiContext(admin);

        return (await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(userId))).SessionVersion;
    }

    // A hospital and a user of it with one role, who signs in with a password of their own.
    private async Task<(FixedIdentity Admin, string Code, UserDto User)> TenantWithUserAsync(DatabaseApiFactory factory, Role role)
    {
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var created = await administrator.PostJsonAsync(Users, new CreateUserRequest { Email = Email, DisplayName = "Meera Nair", Password = Password, Roles = [role] });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var user = await created.ReadAsAsync<UserDto>();

        // The password an administrator set would be changed first (ADR 0051); these tests are about what comes after.
        await using (var direct = AdministratorApiContext(SystemIdentity.Instance))
        {
            await direct.Database.ExecuteSqlAsync($"UPDATE [Users] SET [PasswordChangeDue] = 0 WHERE [Id] = {user.Id}");
        }

        await using var context = ApiContext(admin);

        return (admin, (await context.Set<Tenant>().AsNoTracking().SingleAsync()).Code, user);
    }
}
