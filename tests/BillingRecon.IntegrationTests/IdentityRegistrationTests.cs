using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// Who a request acts for until authentication arrives (Phase 5): a fixed tenant and user from configuration in
/// Development, and nobody anywhere else.
/// </summary>
public sealed class IdentityRegistrationTests
{
    [Fact]
    public void In_development_every_scope_acts_for_the_configured_tenant_and_user()
    {
        using var factory = new DevelopmentApiFactory();
        using var scope = factory.Services.CreateScope();

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        // The values of appsettings.Development.json.
        tenant.ShouldBeOfType<DevelopmentIdentity>();
        tenant.TenantId.ShouldBe(new TenantId(Guid.Parse("d0000000-0000-4000-8000-000000000001")));
        user.UserId.ShouldBe(new UserId(Guid.Parse("d0000000-0000-4000-8000-000000000002")));
        user.ShouldBeSameAs(tenant);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_there_is_no_identity_and_asking_for_one_fails(string environment)
    {
        using var factory = new EnvironmentApiFactory(environment);
        using var scope = factory.Services.CreateScope();

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        tenant.ShouldBeOfType<UnavailableIdentity>();
        Should.Throw<InvalidOperationException>(() => tenant.TenantId).Message.ShouldContain("authentication is not implemented yet");
        Should.Throw<InvalidOperationException>(() => user.UserId).Message.ShouldContain("authentication is not implemented yet");
    }

    [Fact]
    public void The_development_stub_is_not_even_registered_outside_development()
    {
        using var factory = new ApiFactory();

        factory.Services.GetService<DevelopmentIdentity>().ShouldBeNull();
    }

    [Theory]
    [InlineData("DevelopmentIdentity:TenantId")]
    [InlineData("DevelopmentIdentity:UserId")]
    public void In_development_the_api_refuses_to_start_without_both_ids(string missing)
    {
        using var factory = new IncompleteIdentityApiFactory(missing);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("DevelopmentIdentity:TenantId and DevelopmentIdentity:UserId are both required");
    }

    [Fact]
    public void The_api_starts_outside_development_although_nobody_can_act_for_a_tenant_yet()
    {
        using var factory = new ApiFactory();

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }

    private sealed class EnvironmentApiFactory(string environment) : ApiFactory
    {
        protected override string EnvironmentName => environment;
    }

    private sealed class IncompleteIdentityApiFactory(string missing) : ApiFactory
    {
        protected override string EnvironmentName => Environments.Development;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new(missing, Guid.Empty.ToString()),
        ];
    }
}
