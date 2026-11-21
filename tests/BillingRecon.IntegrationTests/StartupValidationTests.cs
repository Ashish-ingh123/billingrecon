using BillingRecon.Api.Common.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>Options are validated when the host starts: a misconfigured API fails fast instead of failing on first use.</summary>
public sealed class StartupValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_api_refuses_to_start_without_a_connection_string(string? connectionString)
    {
        using var factory = new ConfiguredApiFactory(connectionString, seqUrl: null);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Database:ConnectionString is required");
    }

    [Theory]
    [InlineData("this is not a connection string; Password=probe-secret-77")]
    [InlineData("Database=BillingRecon;Password=probe-secret-77")]
    [InlineData("Server=localhost;Connect Timeout=soon;Password=probe-secret-77")]
    [InlineData("Server=localhost;Connect Timeout=99999999999;Password=probe-secret-77")]
    public void The_api_refuses_to_start_with_a_malformed_connection_string_and_does_not_reveal_it(string connectionString)
    {
        using var factory = new ConfiguredApiFactory(connectionString, seqUrl: null);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Database:ConnectionString is not a valid SQL Server connection string");
        exception.ToString().ShouldNotContain("probe-secret-77");
    }

    [Theory]
    [InlineData("seq-server")]
    [InlineData("/relative/path")]
    [InlineData("ftp://seq.example")]
    public void The_api_refuses_to_start_with_an_unusable_Seq_url(string seqUrl)
    {
        using var factory = new ConfiguredApiFactory(ApiFactory.UnreachableSqlServer, seqUrl);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Seq:ServerUrl must be an absolute http or https URL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a base64 key at all")]
    [InlineData("c2hvcnQta2V5")]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZQ==")]
    public void The_api_refuses_to_start_without_a_signing_key_of_256_bits_and_does_not_reveal_it(string? signingKey)
    {
        // The last two are base64 of 9 and of 31 bytes.
        using var factory = new SigningKeyApiFactory(Environments.Production, signingKey);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Jwt:SigningKey is required");
        if (!string.IsNullOrEmpty(signingKey))
        {
            exception.ToString().ShouldNotContain(signingKey);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_the_api_refuses_to_start_with_the_key_of_the_repository(string environment)
    {
        using var factory = new SigningKeyApiFactory(environment, JwtOptions.DevelopmentSigningKey);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Jwt:SigningKey is the development key");
    }

    [Fact]
    public void In_development_the_api_starts_with_the_key_it_ships_which_is_the_one_refused_elsewhere()
    {
        using var factory = new DevelopmentApiFactory();

        factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey.ShouldBe(JwtOptions.DevelopmentSigningKey);
    }

    private sealed class SigningKeyApiFactory(string environment, string? signingKey) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new(SigningKeySetting, signingKey),
        ];
    }

    private sealed class ConfiguredApiFactory(string? connectionString, string? seqUrl) : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Database:ConnectionString", connectionString),
            new("Seq:ServerUrl", seqUrl),
        ];
    }
}
