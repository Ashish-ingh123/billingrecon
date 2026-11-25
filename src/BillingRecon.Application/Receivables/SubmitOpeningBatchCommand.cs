namespace BillingRecon.Application.Receivables;

/// <summary>The maker submits an opening batch for approval (ADR 0053): nothing is posted until a second person approves it.</summary>
public sealed record SubmitOpeningBatchCommand(DateOnly? CutOverDate, string? BillsCsv, string? AdvancesCsv, string? TrialBalanceCsv, string? Comment);
