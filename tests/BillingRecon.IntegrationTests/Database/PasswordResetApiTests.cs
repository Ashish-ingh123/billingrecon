using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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
/// A forgotten password end to end, on the pipeline that ships and a real SQL Server, with the mail server replaced
/// (ADR 0061, amended): the link of the mail is good once and for 30 minutes and only the last one sent; the answer
/// to asking is the same whoever asks; every session of the user ends; the second step is still asked for; only the
/// hash of the link is kept, and in no audit image; and without a mail server nothing is sent or stored.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed partial class PasswordResetApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Email = "meera@cityhospital.org";
    private const string Password = "a long passphrase of mine";
    private const string Chosen = "another passphrase, chosen later";
    private const string PublicBaseUrl = "https://billing.cityhospital.example";
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Options = new("/api/v1/auth/options", UriKind.Relative);
    private static readonly Uri Forgot = new("/api/v1/auth/forgot-password", UriKind.Relative);
    private static readonly Uri Reset = new("/api/v1/auth/reset-password", UriKind.Relative);
    private static readonly Uri Users = new("/api/v1/users", UriKind.Relative);

    [Fact]
    public async Task ADR0061_the_link_of_the_mail_sets_a_new_password_once_and_ends_every_session()
    {
        var mails = new CapturedMails();
        using var factory = WithMail(mails);
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Cashier);
        var before = await SignInAsync(anonymous, code, Password);

        using (var options = await anonymous.GetAsync(Options))
        {
            (await options.ReadAsAsync<SignInOptionsDto>()).PasswordResetByMail.ShouldBeTrue();
        }

        // Asked with the address in another case: 202, nothing in the body, never cached.
        using var asked = await anonymous.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = code.ToUpperInvariant(), Email = "  MEERA@CityHospital.org " });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        (await asked.Content.ReadAsStringAsync()).ShouldBeEmpty();
        asked.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

        // The mail: to the address the hospital keeps, with a link to the app that carries its parts after the '#'.
        var mail = await mails.NextAsync();
        (mail.To, mail.Subject).ShouldBe((Email, "Choose a new BillingRecon password"));
        mail.Body.ShouldContain("Hello Meera Nair,");
        mail.ToString().ShouldNotContain("token=");
        var (token, hospital) = LinkOf(mail);
        hospital.ShouldBe(code);
        mail.Body.ShouldContain($"{PublicBaseUrl}/reset-password#token={token}&hospital={code}");

        // At rest: the hash of the link and when it ends, never the link.
        await using (var context = ApiContext(admin))
        {
            var row = await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id));
            row.PasswordResetTokenHash.ShouldNotBeNull().Length.ShouldBe(64);
            row.PasswordResetTokenHash.ShouldNotContain(token);
            row.PasswordResetExpiresAtUtc.ShouldBe(factory.Clock.UtcNow.AddMinutes(30));
        }

        // Asking changed nothing else: the password and the session are as they were.
        using (var still = await anonymous.PostWithTokenAsync(SecondStepApi.Enrolment, before.AccessToken))
        {
            still.StatusCode.ShouldBe(HttpStatusCode.OK, await still.Content.ReadAsStringAsync());
        }

        // A password that is too short is refused, and the link is still good afterwards.
        using var tooShort = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = token, NewPassword = "short" });
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var reset = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = token, NewPassword = Chosen });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
        reset.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

        // Every session ended (ADR 0057); the old password is gone and the new one signs in, with no change due.
        using var ended = await anonymous.PostWithTokenAsync(SecondStepApi.Enrolment, before.AccessToken);
        await ended.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        using var old = await anonymous.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = Password });
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SignInAsync(anonymous, code, Chosen)).CredentialsChangeDue.ShouldBeFalse();

        // Once: the same link again is refused, and the password stays the chosen one.
        using var again = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = token, NewPassword = Password });
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PasswordResetLinkInvalidCode);
        await SignInAsync(anonymous, code, Chosen);

        // The audit trail says both, as the user, and carries neither the link nor its hash.
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var trail = await TrailAsync(administrator, user.Id);
        trail.Select(record => record.Action).ShouldContain("PasswordResetRequested");
        trail.Select(record => record.Action).ShouldContain("PasswordResetByLink");
        trail.ShouldAllBe(record => !record.After.GetRawText().Contains("PasswordResetTokenHash", StringComparison.Ordinal)
            && !record.After.GetRawText().Contains(token, StringComparison.Ordinal)
            && (record.Before == null || !record.Before.Value.GetRawText().Contains("PasswordResetTokenHash", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ADR0061_a_link_is_refused_after_30_minutes_and_once_a_later_one_was_sent()
    {
        var mails = new CapturedMails();
        using var factory = WithMail(mails);
        using var anonymous = factory.CreateClient();
        var (_, code, _) = await TenantWithUserAsync(factory, Role.Cashier);

        var (first, _) = LinkOf(await AskAsync(anonymous, mails, code));
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        using var late = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = first, NewPassword = Chosen });
        await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PasswordResetLinkInvalidCode);

        // Two more: the earlier of them is replaced by the later, which is the only good one.
        var (second, _) = LinkOf(await AskAsync(anonymous, mails, code));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var (third, _) = LinkOf(await AskAsync(anonymous, mails, code));
        using var replaced = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = second, NewPassword = Chosen });
        await replaced.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PasswordResetLinkInvalidCode);

        // The same refusal for another hospital's code, a text that is no link, and a link changed in one character.
        foreach (var (tenant, token) in new[] { ("no-such-hospital", third), (code, "not-a-link"), (code, third[..^1] + (third[^1] == 'A' ? 'B' : 'A')) })
        {
            using var refused = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = tenant, Token = token, NewPassword = Chosen });
            await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PasswordResetLinkInvalidCode);
        }

        await SignInAsync(anonymous, code, Password);
        factory.Clock.Advance(TimeSpan.FromMinutes(29));
        using var good = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = third, NewPassword = Chosen });
        good.StatusCode.ShouldBe(HttpStatusCode.NoContent, await good.Content.ReadAsStringAsync());
        await SignInAsync(anonymous, code, Chosen);
    }

    [Fact]
    public async Task ADR0061_asking_answers_the_same_for_anyone_and_a_user_is_sent_five_links_an_hour()
    {
        var mails = new CapturedMails();
        using var factory = WithMail(mails);
        using var anonymous = factory.CreateClient();
        var (_, code, _) = await TenantWithUserAsync(factory, Role.Cashier);

        // An unknown hospital and an unknown address: 202 and an empty body, as for a user there is.
        foreach (var (tenant, email) in new[] { ("no-such-hospital", Email), (code, "nobody@cityhospital.org") })
        {
            using var unknown = await anonymous.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = tenant, Email = email });
            unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            (await unknown.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }

        // Five links in the hour, each its own. The first mail there is belongs to the user: the two above sent none.
        var links = new List<string>();
        for (var asked = 0; asked < User.PasswordResetRequestsAllowed; asked++)
        {
            var mail = await AskAsync(anonymous, mails, code);
            mail.To.ShouldBe(Email);
            links.Add(LinkOf(mail).Token);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
        }

        links.Distinct().Count().ShouldBe(User.PasswordResetRequestsAllowed);

        // The sixth answers the same and sends nothing: the fifth link is still the good one.
        using var sixth = await anonymous.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = code, Email = Email });
        sixth.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await sixth.Content.ReadAsStringAsync()).ShouldBeEmpty();

        // An hour after the first, a link is sent again, and it is the next mail there is.
        factory.Clock.Advance(TimeSpan.FromMinutes(36));
        var later = LinkOf(await AskAsync(anonymous, mails, code)).Token;
        links.ShouldNotContain(later);
        using var reset = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = later, NewPassword = Chosen });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADR0061_a_password_set_by_the_link_does_not_replace_the_second_step()
    {
        var mails = new CapturedMails();
        using var factory = WithMail(mails);
        using var anonymous = factory.CreateClient();
        var (_, code, _) = await TenantWithUserAsync(factory, Role.Supervisor);
        var (secret, _) = await anonymous.EnrolAsync((await SignInAsync(anonymous, code, Password)).AccessToken, factory.Clock.UtcNow);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var (token, _) = LinkOf(await AskAsync(anonymous, mails, code));
        using var reset = await anonymous.PostAsJsonAsync(Reset, new ResetPasswordRequest { Tenant = code, Token = token, NewPassword = Chosen });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());

        // The new password alone answers a token that only gives the code; the code of the same app completes it.
        var pending = await SignInAsync(anonymous, code, Chosen);
        pending.SecondStep.ShouldBe(SecondStepStage.Pending);
        using var given = await anonymous.PostWithTokenAsync(SecondStepApi.SecondStep, pending.AccessToken, new SecondStepCodeRequest { Code = SecondStepApi.CodeAt(secret, factory.Clock.UtcNow) });
        given.StatusCode.ShouldBe(HttpStatusCode.OK, await given.Content.ReadAsStringAsync());
        (await given.ReadAsAsync<SignedInDto>()).SecondStep.ShouldBe(SecondStepStage.Done);
    }

    [Fact]
    public async Task ADR0061_without_a_mail_server_the_sign_in_page_is_told_so_and_asking_stores_and_sends_nothing()
    {
        var mails = new CapturedMails();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services => services.AddSingleton<IMailTransport>(mails));
        using var anonymous = factory.CreateClient();
        var (admin, code, user) = await TenantWithUserAsync(factory, Role.Cashier);

        using var options = await anonymous.GetAsync(Options);
        (await options.ReadAsAsync<SignInOptionsDto>()).PasswordResetByMail.ShouldBeFalse();

        using var asked = await anonymous.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = code, Email = Email });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await asked.Content.ReadAsStringAsync()).ShouldBeEmpty();

        await using var context = ApiContext(admin);
        var row = await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id));
        (row.PasswordResetTokenHash, row.PasswordResetExpiresAtUtc, row.PasswordResetRequests).ShouldBe((null, null, 0));
        await Should.ThrowAsync<TimeoutException>(() => mails.NextAsync(TimeSpan.FromMilliseconds(500)));
    }

    [GeneratedRegex(@"/reset-password#token=(?<token>[A-Za-z0-9_-]+)&hospital=(?<hospital>\S+)")]
    private static partial Regex Link();

    private static (string Token, string Hospital) LinkOf(QueuedMail mail)
    {
        var match = Link().Match(mail.Body);
        match.Success.ShouldBeTrue("the mail carries no link");

        return (match.Groups["token"].Value, match.Groups["hospital"].Value);
    }

    private DatabaseApiFactory WithMail(CapturedMails mails)
        => new(SqlServer, configure: services =>
        {
            services.Configure<MailOptions>(options =>
            {
                options.Host = "smtp.cityhospital.example";
                options.From = "billing@cityhospital.example";
                options.PublicBaseUrl = PublicBaseUrl + "/";
            });
            services.AddSingleton<IMailTransport>(mails);
        });

    private static async Task<QueuedMail> AskAsync(HttpClient client, CapturedMails mails, string code)
    {
        using var asked = await client.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = code, Email = Email });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        return await mails.NextAsync();
    }

    private static async Task<SignedInDto> SignInAsync(HttpClient client, string code, string password)
    {
        using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = code, Email = Email, Password = password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<SignedInDto>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=User&entityId={id}&pageSize=100", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
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
