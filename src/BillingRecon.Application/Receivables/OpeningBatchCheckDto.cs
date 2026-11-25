using BillingRecon.Application.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What the files of an opening batch would bring in (ADR 0053), and every row that stops them. <see cref="Accepted"/>
/// is true when nothing is rejected and the totals are those of the trial balance: only then can the batch be submitted.
/// </summary>
/// <param name="CutOverDate">The cut-over date the files were checked for.</param>
/// <param name="Accepted">Whether the files can be submitted as they are.</param>
/// <param name="Receivables">How many opening receivables.</param>
/// <param name="ReceivableTotal">Their sum.</param>
/// <param name="Advances">How many opening advances, credit balances included.</param>
/// <param name="AdvanceTotal">Their sum.</param>
/// <param name="CreditBalanceTotal">The part of <paramref name="AdvanceTotal"/> that was a credit balance in the system before.</param>
/// <param name="Rejected">Every finding, with its file, line and reason.</param>
public sealed record OpeningBatchCheckDto(
    DateOnly CutOverDate,
    bool Accepted,
    int Receivables,
    decimal ReceivableTotal,
    int Advances,
    decimal AdvanceTotal,
    decimal CreditBalanceTotal,
    IReadOnlyList<ImportFinding> Rejected);
