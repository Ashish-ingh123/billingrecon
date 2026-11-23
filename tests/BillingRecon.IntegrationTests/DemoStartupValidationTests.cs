using BillingRecon.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The demo of the one-command local run is off unless asked for, and on only with a usable password for its administrator
/// (ADR 0045): the API refuses to start otherwise, as for every other setting.
/// </summary>
public sealed class DemoStartupValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void With_the_demo_on_the_api_refuses_to_start_without_a_password_of_twelve_characters(string? password)
    {
        using var factory = new DemoApiFactory(("Demo:Enabled", "true"), ("Demo:AdminPassword", password));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(DemoOptions.PasswordMessage);
    }

    [Fact]
    public void The_demo_is_off_unless_asked_for()
    {
        using var factory = new DemoApiFactory();

        factory.CreateClient().Dispose();
        factory.Services.GetRequiredService<IOptions<DemoOptions>>().Value.Enabled.ShouldBeFalse();
    }

    private sealed class DemoApiFactory(params (string Key, string? Value)[] settings) : ApiFactory
    {
        protected override string EnvironmentName => Environments.Production;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            .. settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)),
        ];
    }
}
