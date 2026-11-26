using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The ledger with the postings of some kinds gated: a posting locks the cached balances of its payer until the
/// commit, and a use case posts only after it has read its documents.
/// </summary>
public sealed class RaceGatedLedger(IReceivablesLedger inner, RaceGate gate, IReadOnlyCollection<LedgerPostingKind> gated) : IReceivablesLedger
{
    /// <inheritdoc />
    public Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        return gated.Contains(template.Kind)
            ? gate.AfterAsync(() => inner.PostAsync(template, ct), ct)
            : inner.PostAsync(template, ct);
    }
}
