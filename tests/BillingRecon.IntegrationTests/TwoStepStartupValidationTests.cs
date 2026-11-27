using System.Security.Cryptography;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The settings of the second step of a sign-in are checked when the host starts (ADR 0061): its key is 256 bits and
/// never the one of the repository outside Development, and the requirement is switched off in the Development and
/// Demo environments only. And the key does what it is for: a secret opens for the user it was protected for alone.
/// </summary>
public sealed class TwoStepStartupValidationTests
{
    private static readonly string GoodKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TwoStepOptions.KeyBytes));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a base64 key at all")]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZQ==")]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWYwMQ==")]
    public void The_api_refuses_to_start_without_a_key_of_exactly_256_bits_and_does_not_reveal_it(string? key)
    {
        // The last two are base64 of 31 and of 34 bytes.
        using var factory = new TwoStepApiFactory(Environments.Production, key, required: null);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("TwoStep:EncryptionKey is required");
        if (!string.IsNullOrEmpty(key))
        {
            exception.ToString().ShouldNotContain(key);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Demo")]
    public void Outside_development_the_api_refuses_to_start_with_the_key_of_the_repository(string environment)
    {
        using var factory = new TwoStepApiFactory(environment, TwoStepOptions.DevelopmentEncryptionKey, required: null);

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("TwoStep:EncryptionKey is the development key");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_requirement_is_not_switched_off_where_a_hospital_works(string environment)
    {
        using var factory = new TwoStepApiFactory(environment, GoodKey, required: "false");

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("TwoStep:Required may be false in the Development and Demo environments only");
    }

    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("Production", "true", true)]
    [InlineData("Demo", null, true)]
    [InlineData("Demo", "false", false)]
    public void The_requirement_is_in_force_unless_the_demo_switches_it_off(string environment, string? required, bool inForce)
    {
        using var factory = new TwoStepApiFactory(environment, GoodKey, required);
        using var client = factory.CreateClient();

        var policy = factory.Services.GetRequiredService<ISecondStepPolicy>();
        policy.IsRequired.ShouldBe(inForce);
        policy.IsRequiredFor([Domain.Identity.Role.Supervisor]).ShouldBe(inForce);
        policy.IsRequiredFor([Domain.Identity.Role.TenantAdmin, Domain.Identity.Role.Cashier]).ShouldBe(inForce);
        policy.IsRequiredFor([Domain.Identity.Role.Cashier, Domain.Identity.Role.BillingClerk, Domain.Identity.Role.Viewer]).ShouldBeFalse();
    }

    [Fact]
    public void A_protected_secret_opens_for_its_own_user_alone_and_never_reads_as_the_secret()
    {
        using var factory = new TwoStepApiFactory(Environments.Production, GoodKey, required: null);
        using var client = factory.CreateClient();
        var secrets = factory.Services.GetRequiredService<ISecondStepSecrets>();
        var tenant = new TenantId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        var secret = secrets.NewSecret();

        var stored = secrets.Protect(tenant, user, secret);

        secret.Length.ShouldBe(20);
        stored.ShouldStartWith("v1.");
        stored.Length.ShouldBeLessThanOrEqualTo(Domain.Identity.User.ProtectedSecretMaxLength);
        stored.ShouldNotContain(Convert.ToBase64String(secret));
        secrets.Protect(tenant, user, secret).ShouldNotBe(stored, "a fresh nonce each time");
        secrets.Unprotect(tenant, user, stored).ShouldBe(secret);

        // Another user's row, another hospital, a changed value, and a value of another key: none of them opens.
        secrets.Unprotect(tenant, new UserId(Guid.NewGuid()), stored).ShouldBeNull();
        secrets.Unprotect(new TenantId(Guid.NewGuid()), user, stored).ShouldBeNull();
        secrets.Unprotect(tenant, user, stored[..^4] + (stored.EndsWith("AAA=", StringComparison.Ordinal) ? "BBB=" : "AAA=")).ShouldBeNull();
        secrets.Unprotect(tenant, user, "v2." + stored[3..]).ShouldBeNull();
        secrets.Unprotect(tenant, user, "nonsense").ShouldBeNull();
        using var other = new TwoStepApiFactory(Environments.Production, Convert.ToBase64String(RandomNumberGenerator.GetBytes(TwoStepOptions.KeyBytes)), required: null);
        using var otherClient = other.CreateClient();
        other.Services.GetRequiredService<ISecondStepSecrets>().Unprotect(tenant, user, stored).ShouldBeNull();

        // Recovery codes: ten, each 80 random bits, found by a hash that ignores case and dashes.
        var codes = secrets.NewRecoveryCodes();
        codes.Count.ShouldBe(10);
        codes.Distinct().Count().ShouldBe(10);
        codes.ShouldAllBe(code => code.Length == 19);
        var hash = secrets.HashRecoveryCode(codes[0]);
        hash.Length.ShouldBe(64);
        secrets.HashRecoveryCode(codes[0].Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant()).ShouldBe(hash);
        secrets.HashRecoveryCode(codes[1]).ShouldNotBe(hash);
        (10 * 64 + 9).ShouldBeLessThanOrEqualTo(Domain.Identity.User.RecoveryCodeHashesMaxLength);
    }

    private sealed class TwoStepApiFactory(string environment, string? key, string? required) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,

            // An empty value is how a missing one is given: a null would leave the host's own key in place.
            new(EncryptionKeySetting, key ?? string.Empty),
            .. required is null ? Array.Empty<KeyValuePair<string, string?>>() : [new KeyValuePair<string, string?>("TwoStep:Required", required)],
        ];
    }
}
