using System.Data;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The receivables subledger (ADR 0005; BR-401 to BR-406). A posting is built and checked by
/// <see cref="LedgerPosting"/>, added to the context of the request, and saved by the commit of the use case, with the
/// document it belongs to. The cached balances of the payer move here, at once, in the transaction of the request:
/// the row of the payer is read under an update lock and then changed, in one batch. If no transaction is open yet
/// this class opens it, and the unit of work commits it, exactly as with a document number (ADR 0008). So the row
/// stays locked until the commit, a second posting for the same payer waits for the first instead of overwriting
/// it, and a use case that fails afterwards takes the change back with everything else (BR-007).
/// <para>
/// Lock order, which every posting use case keeps: the number series first, then the balances of the payer, both
/// before the save. The rows of the document are written by the save, after both.
/// </para>
/// <para>
/// Before anything is written, a posting that lowers the balance of a bill, a payment or a credit note is checked
/// against the ledger so far, the entries of this request included: it may not take the balance below zero (BR-405).
/// The document checks its own cap first, under its row version; this is the ledger's own second look.
/// </para>
/// <para>
/// This is one of the files of the production code that send SQL of their own (ADR 0021, BannedSymbolsTests): Entity
/// Framework has no lock hints, and cannot say "add to the column" without the banned <c>ExecuteUpdate</c>; reading
/// the row to write it back would lose one of two concurrent postings. The batch carries the tenant in every WHERE
/// clause, taken from the current scope; every value is a parameter and the text is a constant.
/// </para>
/// </summary>
public sealed class ReceivablesLedger(
    BillingReconDbContext context,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IClock clock) : IReceivablesLedger
{
    // The read takes the update lock on the one row and holds it to the end of the transaction; the change follows
    // under that lock. A payer without the row gets nothing changed, and the answer says so.
    private const string MoveBalances =
        """
        SET NOCOUNT ON;

        DECLARE @found int = 0;

        SELECT @found = 1
        FROM   [dbo].[PayerBalances] WITH (UPDLOCK, ROWLOCK)
        WHERE  [TenantId] = @tenant AND [PayerId] = @payer;

        IF @found = 1
            UPDATE [dbo].[PayerBalances]
            SET    [Receivable] = [Receivable] + @receivable,
                   [UnappliedCash] = [UnappliedCash] + @unappliedCash,
                   [CreditAvailable] = [CreditAvailable] + @creditAvailable,
                   [UpdatedAtUtc] = @now
            WHERE  [TenantId] = @tenant AND [PayerId] = @payer;

        SELECT @found AS [Value];
        """;

    /// <inheritdoc />
    public async Task<Result> PostAsync(PostingTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("A ledger posting was asked for in a scope that has no tenant.");

        var now = clock.UtcNow;
        var posting = LedgerPosting.Create(
            template.Kind,
            template.DocumentType,
            template.DocumentId,
            template.PayerId,
            template.BusinessDate,
            template.Description,
            template.Lines,
            now,
            currentUser.UserId,
            correlation.CorrelationId,
            template.ReversalOf);

        foreach (var (account, subject, change) in posting.SubjectChanges())
        {
            if (change.IsNegative && (await BalanceAsync(account, subject, ct) + change).IsNegative)
            {
                return ReceivablesErrors.LedgerBalanceNegative();
            }
        }

        var payerChange = posting.PayerChange();
        if (!payerChange.IsNone)
        {
            await MoveBalancesAsync(tenantId, posting.PayerId, payerChange, now, ct);
        }

        context.Set<LedgerPosting>().Add(posting);

        return Result.Success();
    }

    // The balance of one subject account: what is saved, plus what this request has posted and not saved yet.
    private async Task<Money> BalanceAsync(LedgerAccount account, LedgerSubject subject, CancellationToken ct)
    {
        var saved = await context.Set<LedgerEntry>()
            .AsNoTracking()
            .Where(entry => entry.Account == account && entry.SubjectType == subject.Type && entry.SubjectId == subject.Id)
            .ToListAsync(ct);

        var pending = context.ChangeTracker.Entries<LedgerEntry>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .Where(entry => entry.Account == account && entry.Subject() == subject);

        return saved.Concat(pending).Select(entry => entry.BalanceChange()).Sum();
    }

    private async Task MoveBalancesAsync(TenantId tenantId, PayerId payerId, PayerBalanceChange change, DateTimeOffset now, CancellationToken ct)
    {
        // The transaction of the use case: opened here if nothing has opened it yet, and committed by the unit of work.
        // If the request ends without a commit, the context is disposed and the transaction rolls back.
        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(ct);
        }

        object[] parameters =
        [
            Amount("@receivable", change.Receivable),
            Amount("@unappliedCash", change.UnappliedCash),
            Amount("@creditAvailable", change.CreditAvailable),
            new SqlParameter("@now", SqlDbType.DateTimeOffset) { Value = now },
            new SqlParameter("@tenant", tenantId.Value),
            new SqlParameter("@payer", payerId.Value),
        ];

#pragma warning disable RS0030 // Sanctioned raw SQL: the locked read and change of a payer's cached balances (ADR 0005, ADR 0021).
        var found = await context.Database.SqlQueryRaw<int>(MoveBalances, parameters).ToListAsync(ct);
#pragma warning restore RS0030

        if (found is not [1])
        {
            throw new InvalidOperationException(
                $"Payer {payerId} has no row of cached balances, so the posting cannot move them. Every payer gets the row when it is created (BR-404).");
        }
    }

    private static SqlParameter Amount(string name, Money amount)
        => new(name, SqlDbType.Decimal) { Precision = 18, Scale = Money.Scale, Value = amount.Amount };
}
