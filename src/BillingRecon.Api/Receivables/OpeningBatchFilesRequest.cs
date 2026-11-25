using System.Text.Json.Serialization;
using BillingRecon.Application.Receivables;

namespace BillingRecon.Api.Receivables;

/// <summary>
/// The files of an opening batch (ADR 0053), each as the text of a CSV file, with the cut-over date they are as at the
/// day before. The body of the dry run and of the submission; unknown members are refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBatchFilesRequest
{
    /// <summary>The first day on which documents are raised here.</summary>
    public DateOnly? CutOverDate { get; init; }

    /// <summary>The unpaid bills: legacy_number, mrn, bill_date, outstanding_amount, and optionally guarantor_ref and due_date.</summary>
    public string? BillsCsv { get; init; }

    /// <summary>The advances and credit balances: legacy_reference, received_date, amount, method, one of mrn and guarantor_ref, and optionally kind.</summary>
    public string? AdvancesCsv { get; init; }

    /// <summary>The trial balance: account (Receivable, Advance, Credit) and amount.</summary>
    public string? TrialBalanceCsv { get; init; }

    /// <summary>What the maker says to the checker; read by the submission only.</summary>
    public string? Comment { get; init; }

    /// <summary>The dry run of these files.</summary>
    public CheckOpeningBatchQuery ToQuery() => new(CutOverDate, BillsCsv, AdvancesCsv, TrialBalanceCsv);

    /// <summary>The submission of these files.</summary>
    public SubmitOpeningBatchCommand ToCommand() => new(CutOverDate, BillsCsv, AdvancesCsv, TrialBalanceCsv, Comment);
}
