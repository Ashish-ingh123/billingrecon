using System.Text;
using BillingRecon.Api.Common.Errors;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace BillingRecon.Api.Common.Idempotency;

/// <summary>
/// Makes an action run at most once per <c>Idempotency-Key</c> (BR-803, ADR 0006). It wraps the whole of the action,
/// model binding and the writing of the result included:
/// <list type="number">
/// <item>No key, or one that is not a single UUID: 400 <c>idempotency_key_missing</c>, and nothing runs.</item>
/// <item>The key is reserved. If it is known already: the stored response is replayed with
/// <c>Idempotent-Replayed: true</c>; or 409 <c>idempotency_in_progress</c> while the first request still runs; or 422
/// <c>idempotency_key_reuse</c> when the key belongs to a different request.</item>
/// <item>The action runs inside the business transaction the reservation opened, and its response is written into a
/// buffer, not to the client.</item>
/// <item>A success (2xx) is stored with the key and committed together with the work of the action; only then does
/// the client get the response. A refusal (4xx) rolls the work back and is stored, so that a retry gets the same
/// refusal. A fault (5xx, an exception, a client that went away) rolls back and frees the key for a retry.</item>
/// </list>
/// The response is held back until the commit because a response that has left cannot be taken back: a client must
/// never be told of a success that then fails to commit.
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store) : IAsyncResourceFilter
{
    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var request = http.Request;

        if (!IdempotencyHeaders.TryParseKey(request.Headers[IdempotencyHeaders.Key], out var parsed))
        {
            context.Result = Refusal(http, CommonErrors.IdempotencyKeyMissing());
            return;
        }

        var key = parsed.Value;
        var body = await ReadBodyAsync(request, http.RequestAborted);
        var pathAndQuery = request.PathBase + request.Path + request.QueryString;
        var fingerprint = RequestFingerprint.Compute(request.Method, pathAndQuery, request.Headers.IfMatch.ToString(), body);

        var reservation = await store.ReserveAsync(key, request.Method, request.PathBase + request.Path, fingerprint, http.RequestAborted);
        switch (reservation.Outcome)
        {
            case IdempotencyOutcome.Replay:
                context.Result = new ReplayResult(reservation.Response ?? throw new InvalidOperationException("A replay has no stored response."));
                return;
            case IdempotencyOutcome.InProgress:
                context.Result = Refusal(http, CommonErrors.IdempotencyInProgress());
                return;
            case IdempotencyOutcome.Reuse:
                context.Result = Refusal(http, CommonErrors.IdempotencyKeyReuse());
                return;
            case IdempotencyOutcome.Reserved:
                break;
            default:
                throw new InvalidOperationException($"Unknown idempotency outcome {reservation.Outcome}.");
        }

        await RunOnceAsync(http, key, next);
    }

    // The key is reserved and the business transaction is open. From here every path ends it: complete, reject or
    // release. The clean-up is never cancelled: a client that goes away must not leave a key in progress.
    private async Task RunOnceAsync(HttpContext http, Guid key, ResourceExecutionDelegate next)
    {
        var response = http.Response;
        var client = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;

        try
        {
            ResourceExecutedContext executed;
            try
            {
                executed = await next();
            }
            catch
            {
                await store.ReleaseAsync(key, CancellationToken.None);
                throw;
            }

            if (executed.Canceled || (executed.Exception is not null && !executed.ExceptionHandled))
            {
                // The exception goes on to the exception handler, which answers 500; the key is free again.
                await store.ReleaseAsync(key, CancellationToken.None);
                return;
            }

            var stored = new StoredResponse(
                response.StatusCode,
                response.ContentType,
                response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
                response.Headers.ETag.ToString() is { Length: > 0 } etag ? etag : null,
                Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));

            switch (response.StatusCode)
            {
                case >= 200 and < 300:
                    try
                    {
                        await store.CompleteAsync(key, stored, CancellationToken.None);
                    }
                    catch
                    {
                        // Nothing was committed: neither the work nor the answer. The client gets a 500, not the
                        // success in the buffer, and may retry with the same key.
                        await store.ReleaseAsync(key, CancellationToken.None);
                        throw;
                    }

                    break;
                case >= 400 and < 500:
                    await store.RejectAsync(key, stored, CancellationToken.None);
                    break;
                default:
                    await store.ReleaseAsync(key, CancellationToken.None);
                    break;
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(client, http.RequestAborted);
        }
        finally
        {
            response.Body = client;
        }
    }

    // The body is read here for the fingerprint and rewound, so that model binding reads it again from the start.
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();

        using var copy = new MemoryStream();
        await request.Body.CopyToAsync(copy, ct);
        request.Body.Position = 0;

        return copy.ToArray();
    }

    private static ObjectResult Refusal(HttpContext http, Error error)
    {
        var problem = error.ToProblemDetails(http);

        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    /// <summary>Writes a stored response again, as it was, and marks it as a replay.</summary>
    private sealed class ReplayResult(StoredResponse stored) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;

            response.StatusCode = stored.StatusCode;
            response.ContentType = stored.ContentType;
            response.Headers[IdempotencyHeaders.Replayed] = "true";

            if (stored.Location is not null)
            {
                response.Headers.Location = stored.Location;
            }

            if (stored.ETag is not null)
            {
                response.Headers[HeaderNames.ETag] = stored.ETag;
            }

            if (stored.Body.Length > 0)
            {
                await response.WriteAsync(stored.Body, Encoding.UTF8, context.HttpContext.RequestAborted);
            }
        }
    }
}
