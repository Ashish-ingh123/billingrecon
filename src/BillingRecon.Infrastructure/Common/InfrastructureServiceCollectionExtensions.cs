using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Infrastructure.Common.Health;
using BillingRecon.Infrastructure.Common.Idempotency;
using BillingRecon.Infrastructure.Common.Logging;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Common.Settings;
using BillingRecon.Infrastructure.Common.Time;
using BillingRecon.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace BillingRecon.Infrastructure.Common;

/// <summary>Registers everything the Infrastructure layer provides. Called once, from the API's <c>Program.cs</c>.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Tag of the health checks that decide readiness (<c>/health/ready</c>).</summary>
    public const string ReadinessTag = "ready";

    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Adds strongly typed options (validated at startup), structured logging, the clock, the identity of the
    /// current scope, the EF Core context with its unit of work, and the readiness health check. The ports of the
    /// business modules are registered by each module (for example <c>AddPayers</c>), which this class may not name.
    /// </summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="environment">The environment of the host; it decides which identity is registered.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => DatabaseOptions.IsWellFormed(options.ConnectionString), DatabaseOptions.MalformedMessage)
            .Validate<IHostEnvironment>(
                (options, host) => !options.ApplyMigrationsOnStartup || host.IsDevelopment(),
                DatabaseOptions.MigrationsOutsideDevelopmentMessage)
            .ValidateOnStart();

        services.AddOptions<SeqOptions>()
            .BindConfiguration(SeqOptions.SectionName)
            .Validate(SeqOptions.HasValidServerUrl, SeqOptions.InvalidServerUrlMessage)
            .ValidateOnStart();

        // The logger belongs to this host and is flushed when the host is disposed. The static Log.Logger is left
        // alone, so several hosts in one process (the integration tests) cannot close each other's logger.
        services.AddSerilog(LoggingSetup.Configure, preserveStaticLogger: true);

        // Created here, not on first use: a machine without India's time zone stops the application at startup.
        services.AddSingleton<IClock>(new SystemClock());

        AddIdentity(services, environment);
        AddPersistence(services);

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, failureStatus: null, tags: [ReadinessTag], timeout: ReadinessTimeout);

        return services;
    }

    // Until authentication arrives (Phase 5) there are two identities. Development: a fixed tenant and user from
    // configuration. Everywhere else: none, and asking for one throws, so nothing acts for a tenant by accident.
    private static void AddIdentity(IServiceCollection services, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            services.AddOptions<DevelopmentIdentityOptions>()
                .BindConfiguration(DevelopmentIdentityOptions.SectionName)
                .Validate(DevelopmentIdentityOptions.IsComplete, DevelopmentIdentityOptions.IncompleteMessage)
                .ValidateOnStart();

            services.AddScoped<DevelopmentIdentity>();
            services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<DevelopmentIdentity>());
            services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<DevelopmentIdentity>());

            // The row of that tenant, which every tenant-scoped row refers to, where the database is migrated at startup.
            services.AddHostedService<DevelopmentTenantSeeder>();

            return;
        }

        services.AddSingleton<UnavailableIdentity>();
        services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<UnavailableIdentity>());
        services.AddSingleton<ICurrentUser>(provider => provider.GetRequiredService<UnavailableIdentity>());
    }

    private static void AddPersistence(IServiceCollection services)
    {
        // The context takes the tenant, the user and the clock of its scope and attaches the tenant guard and the
        // audit stamps to itself; nothing has to be wired here for a save to be guarded.
        services.AddDbContext<BillingReconDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

        // One per request, over the request's context: what a use case changed is saved by one commit (BR-007).
        services.AddScoped<RequestTransaction>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Idempotency keys of financial POSTs; the store holds the transaction of the request while a key is reserved
        // (BR-803, ADR 0006).
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        // Gapless document numbers, taken in the transaction this unit of work commits (BR-801, ADR 0008).
        services.AddScoped<INumberSeries, NumberSeriesAllocator>();

        // The settings of the current tenant, for the use cases of every module (BR-205, BR-607).
        services.AddScoped<ITenantSettingsReader, TenantSettingsReader>();

        // Builds the model at startup (an invalid model stops the application) and, in Development, applies migrations.
        services.AddHostedService<DatabaseStartup>();
    }
}
