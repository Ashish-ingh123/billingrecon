using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Infrastructure.Common.Approvals;
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
    /// Adds strongly typed options (validated at startup), structured logging, the clock, what Development adds to
    /// the identity of a request, the EF Core context with its unit of work, and the readiness health check. The
    /// identity of a request itself is the Api's, from the token (ADR 0031). The ports of the business modules are
    /// registered by each module (for example <c>AddPayers</c>), which this class may not name.
    /// </summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="environment">The environment of the host; Development adds the development identity.</param>
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

    // The tenant and the user of a request come from its token, which the Api reads (ADR 0031); this layer registers
    // only what Development adds: the configured development tenant and user, whom a request without a token acts as
    // there, and the seeder of their rows. The users of the tenant and the reads of a sign-in are here in every
    // environment.
    private static void AddIdentity(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddScoped<ILoginUserLookup, LoginUserLookup>();

        // The demo hospital and its administrator, when Demo:Enabled asks for them, in the Demo and Development environments
        // only (ADR 0045, ADR 0052).
        services.AddOptions<DemoOptions>()
            .BindConfiguration(DemoOptions.SectionName)
            .Validate(DemoOptions.IsUsable, DemoOptions.PasswordMessage)
            .Validate(
                options => !options.Enabled || environment.IsDevelopment() || environment.IsEnvironment(DemoOptions.EnvironmentName),
                DemoOptions.EnvironmentMessage)
            .ValidateOnStart();
        services.AddHostedService<DemoTenantSeeder>();

        // What an operator does to a hospital from the server (ADR 0052), and whether the hospital of a request is active,
        // asked on every request and kept for 30 seconds.
        services.AddScoped<ITenantOperations, TenantOperations>();
        services.AddSingleton<TenantStatusCache>();
        services.AddScoped<ITenantStatusReader, TenantStatusReader>();
        services.AddScoped<IUserSessionReader, UserSessionReader>();

        if (environment.IsDevelopment())
        {
            services.AddOptions<DevelopmentIdentityOptions>()
                .BindConfiguration(DevelopmentIdentityOptions.SectionName)
                .Validate(DevelopmentIdentityOptions.IsComplete, DevelopmentIdentityOptions.IncompleteMessage)
                .ValidateOnStart();

            // The rows of that tenant and that user, which every row of Development refers to, where the database is
            // migrated at startup.
            services.AddHostedService<DevelopmentTenantSeeder>();
        }
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

        // Gapless document numbers, taken in the transaction this unit of work commits (BR-801, ADR 0008). How long the
        // request waited for its series and held it is measured, and is the API's Server-Timing when that is on (ADR 0046).
        services.AddScoped<INumberSeries, NumberSeriesAllocator>();
        services.AddScoped<NumberSeriesTimer>();
        services.AddScoped<IRequestTimings>(provider => provider.GetRequiredService<NumberSeriesTimer>());

        // Approval requests: opened and decided by the use cases of their documents, listed for the checker (BR-604, BR-605).
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();

        // The recorded prints of documents (ADR 0054).
        services.AddScoped<IDocumentPrintRepository, DocumentPrintRepository>();
        services.AddScoped<IApprovalRequestReadService, ApprovalRequestReadService>();

        // The settings of the current tenant, for the use cases of every module (BR-205, BR-607).
        services.AddScoped<ITenantSettingsReader, TenantSettingsReader>();

        // The users of the tenant and the hashing of their passwords (BR-601, ADR 0030).
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserReadService, UserReadService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // The tenant's settings, its prefix changes, and the series whose prefix an approved change sets (BR-607,
        // BR-802, ADR 0033). Numbers are still taken by INumberSeries alone.
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantImageRepository, TenantImageRepository>();
        services.AddScoped<IPrintSettingsRepository, PrintSettingsRepository>();
        services.AddScoped<IUserNames, UserNames>();
        services.AddScoped<IPrefixChangeRepository, PrefixChangeRepository>();
        services.AddScoped<IPrefixChangeReadService, PrefixChangeReadService>();
        services.AddScoped<IPrefixUsage, PrefixUsage>();
        services.AddScoped<INumberSeriesRepository, NumberSeriesRepository>();

        // Builds the model at startup (an invalid model stops the application) and, in Development, applies migrations.
        services.AddSingleton<DatabaseSettingsProbe>();
        services.AddHostedService<DatabaseStartup>();
    }
}
