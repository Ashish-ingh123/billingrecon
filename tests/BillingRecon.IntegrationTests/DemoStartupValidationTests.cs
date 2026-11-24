using BillingRecon.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The demo of the one-command local run is off unless asked for, and on only with a usable password for its administrator
/// (ADR 0045), and only in the Demo and Development environments (ADR 0052): the API refuses to start otherwise, as for
/// every other setting.
/// </summary>
public sealed class DemoStartupValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void With_the_demo_on_the_api_refuses_to_start_without_a_password_of_twelve_characters(string? password)
    {
        using var factory = new DemoApiFactory("Demo", ("Demo:Enabled", "true"), ("Demo:AdminPassword", password));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(DemoOptions.PasswordMessage);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ADR0052_the_demo_is_refused_outside_the_demo_and_development_environments(string environment)
    {
        using var factory = new DemoApiFactory(environment, ("Demo:Enabled", "true"), ("Demo:AdminPassword", "a-long-enough-password"));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(DemoOptions.EnvironmentMessage);
    }

    [Fact]
    public void ADR0052_the_demo_starts_in_the_demo_environment()
    {
        using var factory = new DemoApiFactory("Demo", ("Demo:Enabled", "false"));

        factory.CreateClient().Dispose();
        factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.ShouldBe("Demo");
    }

    [Fact]
    public void The_demo_is_off_unless_asked_for()
    {
        using var factory = new DemoApiFactory(Environments.Production);

        factory.CreateClient().Dispose();
        factory.Services.GetRequiredService<IOptions<DemoOptions>>().Value.Enabled.ShouldBeFalse();
    }

    private sealed class DemoApiFactory(string environment, params (string Key, string? Value)[] settings) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            .. settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)),
        ];
    }
}
