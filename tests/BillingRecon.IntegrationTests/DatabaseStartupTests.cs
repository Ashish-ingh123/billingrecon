using System.Data.Common;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Health;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Common.Settings;
using BillingRecon.Infrastructure.Common.Time;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// What the API does with the database layer when it starts: it builds the model (an invalid model stops it), it
/// migrates only in Development and only when told to, and it wires the context with its interceptors.
/// </summary>
public sealed class DatabaseStartupTests
{
    [Fact]
    public void The_api_refuses_to_start_when_a_decimal_column_has_no_precision()
    {
        using var factory = new ProbeModelApiFactory<ExplicitColumnSizeTests.UnsizedDecimal>();

        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Priced.Amount: decimal without an explicit precision and scale");
    }

    [Fact]
    public void The_api_refuses_to_start_when_a_text_column_has_no_maximum_length()
    {
        using var factory = new ProbeModelApiFactory<ExplicitColumnSizeTests.UnsizedString>();

        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Titled.Title: text without a maximum length");
    }

    [Fact]
    public void The_api_starts_with_a_model_whose_columns_are_all_sized()
    {
        using var factory = new ProbeModelApiFactory<ExplicitColumnSizeTests.Sized>();

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Migrations_at_startup_cannot_be_switched_on_outside_development(string environment)
    {
        using var factory = new MigratingApiFactory(environment);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Database:ApplyMigrationsOnStartup may be true in the Development environment only");
    }

    [Fact]
    public void In_development_with_migrations_switched_on_the_api_goes_to_the_database_at_startup()
    {
        using var factory = new MigratingApiFactory(Environments.Development);

        // The database of the tests is a closed port, so "it migrates" shows as "it fails to connect": startup stops.
        var exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.WithInnerExceptions().ShouldContain(inner => inner is DbException, "startup should have failed while connecting to the database");
    }

    [Fact]
    public void In_development_with_migrations_switched_off_the_api_starts_without_touching_the_database()
    {
        // DevelopmentApiFactory switches the setting off, as every test host does; the closed port is never contacted.
        using var factory = new DevelopmentApiFactory();

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }

    [Fact]
    public void The_settings_shipped_for_development_switch_migrations_at_startup_on()
    {
        // Nothing here overrides Database:ApplyMigrationsOnStartup, so appsettings.Development.json decides.
        using var factory = new ShippedSettingsApiFactory(Environments.Development);

        var exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.WithInnerExceptions().ShouldContain(inner => inner is DbException, "startup should have failed while connecting to the database");
    }

    [Fact]
    public void The_settings_shipped_for_other_environments_leave_migrations_at_startup_off()
    {
        // Nothing here overrides the setting either. Shipping it switched on would stop the API at startup.
        using var factory = new ShippedSettingsApiFactory(Environments.Production);

        factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup.ShouldBeFalse();
    }

    [Fact]
    public void The_context_of_a_request_has_both_save_interceptors()
    {
        using var factory = new DevelopmentApiFactory();
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<BillingReconDbContext>();

        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToArray();
        interceptors.Count(interceptor => interceptor is TenantWriteGuardInterceptor).ShouldBe(1);
        interceptors.Count(interceptor => interceptor is AuditStampInterceptor).ShouldBe(1);
    }

    [Fact]
    public void A_context_built_by_hand_has_both_save_interceptors_too()
    {
        // The context attaches them to itself; the dependency-injection set-up adds none.
        using var context = TestContexts.CreateProduction();

        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToArray();
        interceptors.Count(interceptor => interceptor is TenantWriteGuardInterceptor).ShouldBe(1);
        interceptors.Count(interceptor => interceptor is AuditStampInterceptor).ShouldBe(1);
    }

    [Fact]
    public void The_clock_of_the_api_is_the_system_clock()
    {
        using var factory = new ApiFactory();

        factory.Services.GetRequiredService<IClock>().ShouldBeOfType<SystemClock>();
    }

    [Fact]
    public void Readiness_is_decided_by_the_database_check_alone()
    {
        using var factory = new ApiFactory();

        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var ready = registrations.Where(registration => registration.Tags.Contains("ready")).ShouldHaveSingleItem();
        ready.Name.ShouldBe(DatabaseHealthCheck.Name);
    }

    // The tests that expect the API to reach for the database at startup use a connection string of their own, without
    // pooling. On the shared one, a test of another class that is connecting at the same moment can make the pool
    // answer with its own timeout (an InvalidOperationException) instead of the connection failure these tests look for.
    private const string UnpooledUnreachableSqlServer = ApiFactory.UnreachableSqlServer + ";Pooling=False";

    /// <summary>The API with the production context replaced by one whose model has an extra probe entity.</summary>
    private sealed class ProbeModelApiFactory<TProbe> : ApiFactory
        where TProbe : IModelProbe, new()
    {
        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);

            services.AddScoped<BillingReconDbContext>(provider =>
                new ModelProbeContext<TProbe>(
                    TestContexts.Options<ModelProbeContext<TProbe>>(),
                    provider.GetRequiredService<ITenantContext>(),
                    provider.GetRequiredService<ICurrentUser>(),
                    provider.GetRequiredService<IClock>()));
        }
    }

    /// <summary>The API in the given environment with migrations at startup switched on.</summary>
    private sealed class MigratingApiFactory(string environment) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Database:ConnectionString", UnpooledUnreachableSqlServer),
            new("Seq:ServerUrl", string.Empty),
            new("Database:ApplyMigrationsOnStartup", "true"),
        ];
    }

    /// <summary>The API in the given environment with the shipped settings left as they are, apart from the connection string and Seq.</summary>
    private sealed class ShippedSettingsApiFactory(string environment) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Database:ConnectionString", UnpooledUnreachableSqlServer),
            new("Seq:ServerUrl", string.Empty),
        ];
    }
}
