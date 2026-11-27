using System.Net;
using System.Net.Http.Json;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The mail server of a forgotten password is optional and checked when the host starts (ADR 0061, amended): no
/// server is a supported way to run, a server without a sender or without the https address of the app is refused,
/// and its password is in no message. And the tokens of the links: random, naming their user, read back or refused.
/// </summary>
public sealed class MailStartupValidationTests
{
    private static readonly Uri Options = new("/api/v1/auth/options", UriKind.Relative);
    private static readonly Uri Forgot = new("/api/v1/auth/forgot-password", UriKind.Relative);
    private static readonly Uri Reset = new("/api/v1/auth/reset-password", UriKind.Relative);

    [Fact]
    public async Task Without_a_mail_server_the_api_starts_says_so_and_answers_a_request_for_a_link_as_always()
    {
        using var factory = new MailApiFactory(Environments.Production, host: null, from: null, publicBaseUrl: null);
        using var client = factory.CreateClient();

        using var options = await client.GetAsync(Options);
        options.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await options.ReadAsAsync<SignInOptionsDto>()).PasswordResetByMail.ShouldBeFalse();

        using var asked = await client.PostAsJsonAsync(Forgot, new ForgotPasswordRequest { Tenant = "city-hospital", Email = "meera@cityhospital.org" });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await asked.Content.ReadAsStringAsync()).ShouldBeEmpty();
        asked.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task With_a_complete_mail_server_the_api_starts_and_says_a_link_can_be_sent()
    {
        using var factory = new MailApiFactory(Environments.Production, "smtp.cityhospital.example", "billing@cityhospital.example", "https://billing.cityhospital.example");
        using var client = factory.CreateClient();

        using var options = await client.GetAsync(Options);

        (await options.ReadAsAsync<SignInOptionsDto>()).PasswordResetByMail.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production", null, "https://billing.cityhospital.example")]
    [InlineData("Production", "not an address", "https://billing.cityhospital.example")]
    [InlineData("Production", "billing@cityhospital.example", null)]
    [InlineData("Production", "billing@cityhospital.example", "billing.cityhospital.example")]
    [InlineData("Production", "billing@cityhospital.example", "http://billing.cityhospital.example")]
    [InlineData("Demo", "billing@cityhospital.example", "http://billing.cityhospital.example")]
    public void A_mail_server_without_a_sender_or_the_https_address_of_the_app_stops_the_start_and_reveals_no_password(string environment, string? from, string? publicBaseUrl)
    {
        using var factory = new MailApiFactory(environment, "smtp.cityhospital.example", from, publicBaseUrl);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Mail:Host is set, so Mail:From");
        exception.ToString().ShouldNotContain(MailApiFactory.ServerPassword);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    public void A_port_that_is_none_stops_the_start(string port)
    {
        using var factory = new MailApiFactory(Environments.Production, "smtp.cityhospital.example", "billing@cityhospital.example", "https://billing.cityhospital.example", port);

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("Mail:Port is between 1 and 65535");
    }

    [Theory]
    [InlineData("""{"tenant":"city-hospital"}""")]
    [InlineData("""{"tenant":"city-hospital","email":"meera@cityhospital.org","role":"TenantAdmin"}""")]
    public async Task A_request_for_a_link_without_its_parts_or_with_one_too_many_is_refused(string body)
    {
        using var factory = new MailApiFactory(Environments.Production, host: null, from: null, publicBaseUrl: null);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Forgot, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void No_text_of_a_reset_request_or_of_a_mail_carries_the_link_or_the_password()
    {
        var request = new ResetPasswordRequest { Tenant = "city-hospital", Token = "the-token-of-the-link", NewPassword = "a passphrase of mine" };
        var mail = new QueuedMail("meera@cityhospital.org", "Choose a new BillingRecon password", "https://billing.example/reset-password#token=the-token-of-the-link");

        request.ToString().ShouldNotContain("the-token-of-the-link");
        request.ToString().ShouldNotContain("a passphrase of mine");
        request.ToCommand().ToString().ShouldNotContain("the-token-of-the-link");
        request.ToCommand().ToString().ShouldNotContain("a passphrase of mine");
        mail.ToString().ShouldNotContain("the-token-of-the-link");
        mail.ToString().ShouldNotContain("meera@cityhospital.org");
    }

    [Fact]
    public void A_token_names_its_user_is_new_every_time_and_is_found_by_a_hash_that_is_not_the_token()
    {
        var tokens = new PasswordResetTokens();
        var user = new UserId(Guid.NewGuid());

        var (token, hash) = tokens.New(user);
        var (second, secondHash) = tokens.New(user);

        // 48 bytes, 32 of them random, in the base64 of an address: 64 characters with nothing to escape.
        token.Length.ShouldBe(64);
        Uri.EscapeDataString(token).ShouldBe(token);
        (second, secondHash).ShouldNotBe((token, hash));
        hash.Length.ShouldBe(Domain.Identity.User.PasswordResetTokenHashLength);
        hash.ShouldNotContain(token);

        tokens.TryRead($"  {token} ", out var read, out var readHash).ShouldBeTrue();
        (read, readHash).ShouldBe((user, hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-link")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void A_text_that_is_no_token_is_not_read(string? text)
    {
        // The fifth is the right length and names the empty id, which is no user.
        new PasswordResetTokens().TryRead(text, out _, out _).ShouldBeFalse();
        new PasswordResetTokens().TryRead(new string('A', 5000), out _, out _).ShouldBeFalse();
    }

    private sealed class MailApiFactory(string environment, string? host, string? from, string? publicBaseUrl, string? port = null) : ApiFactory
    {
        public const string ServerPassword = "the-password-of-the-mail-server";

        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new("Mail:Host", host ?? string.Empty),
            new("Mail:From", from ?? string.Empty),
            new("Mail:PublicBaseUrl", publicBaseUrl ?? string.Empty),
            new("Mail:Password", ServerPassword),
            .. port is null ? Array.Empty<KeyValuePair<string, string?>>() : [new KeyValuePair<string, string?>("Mail:Port", port)],
        ];

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);

            // Nothing leaves the test, whatever it does.
            services.AddSingleton<IMailTransport>(new CapturedMails());
        }
    }
}
