using System.Security.Cryptography;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// Hosts the API in-process. It runs as Production, the pipeline that ships, unless a derived factory says otherwise.
/// On top of the API's own services it adds the test-only <see cref="ProbeController"/>, a sink that captures the log
/// events (<see cref="Logs"/>) and the raw request target a real server would provide (<see cref="RawTargetStartupFilter"/>).
/// Requests are authenticated as they are in production, by tokens: <see cref="TestClientExtensions"/> makes clients
/// that send one, minted by the host's own issuer (ADR 0031, point 10). They reach the API as a client of a deployment
/// does: over HTTPS, for a host the API answers for (ADR 0035), unless a test says otherwise; and a test can give a
/// request the client address it comes from (<see cref="RemoteAddressStartupFilter"/>).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A well-formed connection string to a port nothing listens on: the host starts, and nothing in these tests
    /// depends on a real SQL Server.
    /// </summary>
    public const string UnreachableSqlServer = "Server=127.0.0.1,1;Integrated Security=true;Encrypt=False;Connect Timeout=1";

    /// <summary>The setting of the signing key.</summary>
    public const string SigningKeySetting = "Jwt:SigningKey";

    /// <summary>The setting of the host names the API answers for (ADR 0035).</summary>
    public const string AllowedHostsSetting = "AllowedHosts";

    /// <summary>The host every client calls, which outside Development is the one <see cref="AllowedHostsSetting"/> names.</summary>
    public const string Host = "localhost";

    /// <summary>Where every client of these hosts sends its requests: HTTPS, as outside Development nothing else is answered.</summary>
    public static readonly Uri BaseAddress = new($"https://{Host}");

    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Outside Development the host needs a key of its own, as a deployment does; every factory makes a new one. It is
    // long enough for HS512 as well, so a test can sign with the right key and the wrong algorithm.
    private readonly string _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(2 * JwtOptions.MinimumKeyBytes));

    /// <summary>Makes <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> call <see cref="BaseAddress"/>.</summary>
    public ApiFactory()
    {
        ClientOptions.BaseAddress = BaseAddress;
    }

    /// <summary>
    /// The key the host signs and checks tokens with: outside Development a random one of this factory, laid under
    /// <see cref="Settings"/> (which may replace it); in Development the key of <c>appsettings.Development.json</c>,
    /// as it ships.
    /// </summary>
    public string SigningKey => EnvironmentName == Environments.Development ? JwtOptions.DevelopmentSigningKey : _signingKey;

    /// <summary>Every log event the API wrote, as the sinks received it.</summary>
    public InMemoryLogSink Logs { get; } = new();

    /// <summary>What the tests of the idempotency filter tell the probe endpoint to do, and what it reports back.</summary>
    public IdempotencyProbeState IdempotencyProbe { get; } = new();

    /// <summary>The ASP.NET Core environment the host runs in.</summary>
    protected virtual string EnvironmentName => Environments.Production;

    /// <summary>Configuration laid over the API's own; it wins over appsettings, environment variables and user-secrets.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Database:ConnectionString", UnreachableSqlServer),

        // Tests never ship logs to a real Seq server, whatever appsettings.Development.json points at.
        new("Seq:ServerUrl", string.Empty),

        // Nor do they migrate a database at startup, which appsettings.Development.json switches on: there is none.
        new("Database:ApplyMigrationsOnStartup", "false"),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Outside Development the host needs, as a deployment does, a key of its own and the hosts it answers for.
            if (EnvironmentName != Environments.Development)
            {
                configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(SigningKeySetting, _signingKey),
                    new KeyValuePair<string, string?>(AllowedHostsSetting, Host),
                ]);
            }

            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureTestServices(ConfigureTestServices);
    }

    /// <summary>
    /// Starts the host so that a startup failure always reaches the test as the exception that caused it.
    /// <para>
    /// The API's own <c>Program</c> starts the host, on another thread. When startup fails at once (a rejected
    /// setting, a model that does not build), that thread disposes the host straight away, and a test that has not
    /// begun to wait for the start yet finds a disposed host: an <see cref="ObjectDisposedException"/> instead of
    /// the failure, now and then, depending on which thread is faster. So the order is fixed here: begin to wait
    /// first, then let the host start (it holds in <see cref="StartWhenObserved"/> until then), then take the outcome.
    /// </para>
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Registered here, after everything else, because the test server registers a lifetime of its own and the
        // last registration is the one the host uses.
        builder.ConfigureServices(services => services.AddSingleton<IHostLifetime>(new StartWhenObserved(_listening.Task)));

        var host = builder.Build();

        var started = host.StartAsync();
        _listening.TrySetResult();

        // CreateHost is synchronous by contract; the base implementation blocks on the start in the same way.
        started.GetAwaiter().GetResult();

        return host;
    }

    /// <summary>Registers what the tests add to the API's own services.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
        services.AddSingleton<ILogEventSink>(Logs);
        services.AddSingleton(IdempotencyProbe);
        services.AddTransient<IStartupFilter, RawTargetStartupFilter>();
        services.AddTransient<IStartupFilter, RemoteAddressStartupFilter>();
    }

    /// <summary>A client made by <see cref="WebApplicationFactory{TEntryPoint}.CreateDefaultClient(DelegatingHandler[])"/> calls <see cref="BaseAddress"/> too.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.BaseAddress = BaseAddress;
    }

    /// <summary>
    /// The first thing a host does when it starts is to ask its lifetime whether it may. This one says yes once the
    /// test is waiting for the outcome. It takes the place of the test server's own lifetime, which does nothing.
    /// </summary>
    private sealed class StartWhenObserved(Task observed) : IHostLifetime
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        public Task WaitForStartAsync(CancellationToken cancellationToken) => observed.WaitAsync(Patience, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
