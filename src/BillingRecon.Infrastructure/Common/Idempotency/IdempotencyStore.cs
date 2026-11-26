using System.Text.Json;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Idempotency;

/// <summary>
/// The idempotency keys of the current tenant in the <c>IdempotencyKeys</c> table (ADR 0006; docs/database.md,
/// section 6).
/// <para>
/// Two kinds of work happen here, on two kinds of context. What must be permanent whatever becomes of the request,
/// the reservation, the stored refusal and the release, each runs on a short-lived context of its own: its own
/// connection, its own transaction, and no knowledge of what the use case has left in the request's change tracker.
/// What must be permanent only together with the work of the request, the stored answer to a success, is saved
/// through the request's own context, inside the business transaction this store opened and holds, and committed
/// here.
/// </para>
/// <para>
/// Concurrent requests with one key are decided by the primary key: one INSERT wins, the others find the row.
/// </para>
/// <para>
/// When it ends a transaction that holds a number series, it tells the <see cref="NumberSeriesTimer"/>, which measures
/// how long the series was held (ADR 0046).
/// </para>
/// </summary>
public sealed class IdempotencyStore(
    BillingReconDbContext context,
    DbContextOptions<BillingReconDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IClock clock,
    RequestTransaction requestTransaction,
    NumberSeriesTimer? numberSeriesTimer = null) : IIdempotencyStore
{
    // SQL Server: 2627 is a duplicate in a primary key or unique constraint, 2601 in a unique index.
    private const int DuplicateInUniqueConstraint = 2627;
    private const int DuplicateInUniqueIndex = 2601;

    // The row of the key this request reserved, tracked by the request's context since the reservation.
    private IdempotencyKey? _reserved;

    /// <inheritdoc />
    public async Task<IdempotencyReservation> ReserveAsync(Guid key, string method, string path, byte[] requestHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requestHash);

        if (tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("An idempotency key was reserved in a scope that has no tenant.");
        }

        for (var attempt = 1; ; attempt++)
        {
            await using var own = NewContext();
            own.Add(IdempotencyKey.Reserve(key, currentUser.UserId, method, path, requestHash, clock.UtcNow));

            try
            {
                await own.SaveChangesAsync(ct);

                break;
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is SqlException { Number: DuplicateInUniqueConstraint or DuplicateInUniqueIndex })
            {
                // Once: a reservation that its process left behind is removed, and this request reserves the key anew.
                if (attempt == 1 && await RemoveAbandonedAsync(key, requestHash, ct))
                {
                    continue;
                }

                return await DecideAsync(key, requestHash, ct);
            }
        }

        // The row is read into the request's context now, before the use case can take a number: completing it then
        // sends no read while a number series is held, which every other posting of the series waits for (ADR 0046).
        _reserved = await context.Set<IdempotencyKey>().SingleAsync(candidate => candidate.Key == key, ct);

        // The key is ours. Everything the use case saves from here on, and the answer, commit together or not at all.
        await context.Database.BeginTransactionAsync(ct);
        requestTransaction.Hold();

        return IdempotencyReservation.Reserved;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid key, StoredResponse response, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);

        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("There is no business transaction to complete the idempotency key in; reserve the key first.");

        var row = _reserved is { } reserved && reserved.Key == key
            ? reserved
            : await context.Set<IdempotencyKey>().SingleAsync(candidate => candidate.Key == key, ct);
        Store(row, response);

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        numberSeriesTimer?.TransactionEnded();
        await EndTransactionAsync();
    }

    /// <inheritdoc />
    public async Task RejectAsync(Guid key, StoredResponse response, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);

        await RollBackAsync();

        await using var own = NewContext();
        var row = await own.Set<IdempotencyKey>().SingleAsync(candidate => candidate.Key == key, ct);
        Store(row, response);

        await own.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid key, CancellationToken ct)
    {
        await RollBackAsync();

        await using var own = NewContext();
        var row = await own.Set<IdempotencyKey>().SingleOrDefaultAsync(candidate => candidate.Key == key, ct);
        if (row is { Status: IdempotencyStatus.InProgress })
        {
            // The one delete besides the lines of a draft bill: a reservation that came to nothing (ADR 0006).
            own.Remove(row);
            await own.SaveChangesAsync(ct);
        }
    }

    // A process that dies between reserving and answering leaves the key in progress for good, and every retry of the
    // request was answered 409 for ever (review 2026-11, finding R3). What it did was rolled back with its connection:
    // only the reservation is left, and after IdempotencyKey.AbandonedAfter the same request may take it over.
    private async Task<bool> RemoveAbandonedAsync(Guid key, byte[] requestHash, CancellationToken ct)
    {
        await using var own = NewContext();
        var row = await own.Set<IdempotencyKey>().SingleOrDefaultAsync(candidate => candidate.Key == key, ct);

        if (row is null || !row.IsFor(requestHash) || !row.IsAbandoned(clock.UtcNow))
        {
            return false;
        }

        own.Remove(row);

        try
        {
            await own.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another retry removed it first; whichever of the two then reserves the key runs.
        }

        return true;
    }

    // The key exists. Whose it is decides the answer; the row that is there is never changed by a later request.
    private async Task<IdempotencyReservation> DecideAsync(Guid key, byte[] requestHash, CancellationToken ct)
    {
        await using var own = NewContext();
        var existing = await own.Set<IdempotencyKey>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Key == key, ct);

        // Released between the refused INSERT and this read: the other request failed with a fault. The client is told
        // to wait and try again, which it may.
        if (existing is null)
        {
            return IdempotencyReservation.InProgress;
        }

        if (!existing.IsFor(requestHash))
        {
            return IdempotencyReservation.Reuse;
        }

        return existing.Status == IdempotencyStatus.Completed
            ? IdempotencyReservation.Replay(ToResponse(existing))
            : IdempotencyReservation.InProgress;
    }

    private void Store(IdempotencyKey row, StoredResponse response)
    {
        var headers = JsonSerializer.Serialize(new StoredHeaders(response.Location, response.ETag));
        row.Complete(response.StatusCode, response.ContentType, headers, response.Body, clock.UtcNow);
    }

    private static StoredResponse ToResponse(IdempotencyKey row)
    {
        var headers = row.ResponseHeaders is null ? null : JsonSerializer.Deserialize<StoredHeaders>(row.ResponseHeaders);

        return new StoredResponse(
            row.StatusCode ?? throw new InvalidOperationException("A completed idempotency key has no status code."),
            row.ResponseContentType,
            headers?.Location,
            headers?.ETag,
            row.ResponseBody ?? string.Empty);
    }

    // Not cancellable: once the request has failed, what it holds is released whatever the caller does next.
    private async Task RollBackAsync()
    {
        if (context.Database.CurrentTransaction is { } transaction)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            numberSeriesTimer?.TransactionEnded();
        }

        await EndTransactionAsync();
    }

    private async Task EndTransactionAsync()
    {
        if (context.Database.CurrentTransaction is { } transaction)
        {
            await transaction.DisposeAsync();
        }

        requestTransaction.Release();
    }

    // The same tenant, user and clock as the request, on a connection of its own.
    private BillingReconDbContext NewContext() => new(options, tenantContext, currentUser, clock);

    private sealed record StoredHeaders(string? Location, string? ETag);
}
