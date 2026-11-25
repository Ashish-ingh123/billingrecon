using BillingRecon.Application.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>The explicit mapping of the opening batch to what the API answers.</summary>
public static class OpeningBatchMapping
{
    /// <summary>The answer for <paramref name="batch"/>.</summary>
    public static OpeningBatchDto ToDto(this OpeningBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new OpeningBatchDto(
            Id: batch.Id.Value,
            CutOverDate: batch.CutOverDate,
            Status: batch.Status,
            Receivables: batch.Receivables,
            ReceivableTotal: batch.ReceivableTotal.Amount,
            Advances: batch.Advances,
            AdvanceTotal: batch.AdvanceTotal.Amount,
            SubmittedAtUtc: batch.CreatedAtUtc,
            SubmittedByUserId: batch.CreatedByUserId?.Value,
            DecidedAtUtc: batch.DecidedAtUtc,
            Version: RowVersionText.Of(batch));
    }

    /// <summary>The answer for the check <paramref name="check"/> of files for <paramref name="cutOverDate"/>.</summary>
    public static OpeningBatchCheckDto ToDto(this OpeningBatchCheck check, DateOnly cutOverDate)
    {
        ArgumentNullException.ThrowIfNull(check);

        return new OpeningBatchCheckDto(
            CutOverDate: cutOverDate,
            Accepted: check.Findings.Count == 0,
            Receivables: check.Receivables.Count,
            ReceivableTotal: check.ReceivableTotal.Amount,
            Advances: check.Advances.Count,
            AdvanceTotal: check.AdvanceTotal.Amount,
            CreditBalanceTotal: check.CreditBalanceTotal.Amount,
            Rejected: check.Findings);
    }
}
