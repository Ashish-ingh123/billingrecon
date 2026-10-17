using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Plays the role of a Seq server: an in-process HTTP endpoint on a free local port that accepts whatever the Seq sink
/// posts and keeps it. It lets the tests prove what is shipped to Seq on a machine without Docker.
/// </summary>
internal sealed class SeqStub : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<Received> _requests = new();

    private SeqStub(WebApplication app) => _app = app;

    /// <summary>The base URL to give the API as <c>Seq:ServerUrl</c>.</summary>
    public Uri Url => new(_app.Urls.Single());

    /// <summary>Everything received so far.</summary>
    public IReadOnlyList<Received> Requests => [.. _requests];

    public static async Task<SeqStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var stub = new SeqStub(app);
        app.Run(stub.HandleAsync);
        await app.StartAsync();

        return stub;
    }

    /// <summary>Waits until a received body satisfies <paramref name="predicate"/> and returns all bodies; fails after the timeout.</summary>
    public async Task<string> WaitForBodyAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var bodies = string.Join('\n', _requests.Select(request => request.Body));
            if (predicate(bodies))
            {
                return bodies;
            }

            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"The Seq stub did not receive the expected events within {timeout}. Received {_requests.Count} request(s).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        _requests.Enqueue(new Received(context.Request.Method, context.Request.Path, context.Request.ContentType, body));

        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{}", context.RequestAborted);
    }

    /// <summary>One request the sink made.</summary>
    internal sealed record Received(string Method, string Path, string? ContentType, string Body);
}
