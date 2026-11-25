namespace BillingRecon.Application.Receivables;

/// <summary>The dry run of an opening batch (ADR 0053): checks the files and writes nothing.</summary>
public sealed record CheckOpeningBatchQuery(DateOnly? CutOverDate, string? BillsCsv, string? AdvancesCsv, string? TrialBalanceCsv);
