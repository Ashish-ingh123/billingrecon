using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The files of an opening batch (ADR 0053), as text: the unpaid bills, the advances and credit balances, and the trial
/// balance of the system before, all as at the close of the day before <see cref="CutOverDate"/>.
/// </summary>
/// <param name="CutOverDate">The first day on which documents are raised here.</param>
/// <param name="BillsCsv">
/// <c>legacy_number</c>, <c>mrn</c>, <c>bill_date</c>, <c>outstanding_amount</c>, and optionally <c>guarantor_ref</c> (who
/// owes it, when not the patient) and <c>due_date</c>.
/// </param>
/// <param name="AdvancesCsv">
/// <c>legacy_reference</c>, <c>received_date</c>, <c>amount</c>, <c>method</c>, one of <c>mrn</c> and
/// <c>guarantor_ref</c>, and optionally <c>kind</c> (<c>Advance</c> or <c>Credit</c>).
/// </param>
/// <param name="TrialBalanceCsv"><c>account</c> (<c>Receivable</c>, <c>Advance</c>, <c>Credit</c>) and <c>amount</c>.</param>
public sealed record OpeningBatchFiles(DateOnly CutOverDate, string? BillsCsv, string? AdvancesCsv, string? TrialBalanceCsv)
{
    /// <summary>The name a finding gives the bills' file.</summary>
    public const string BillsFile = "bills.csv";

    /// <summary>The name a finding gives the advances' file.</summary>
    public const string AdvancesFile = "advances.csv";

    /// <summary>The name a finding gives the trial balance.</summary>
    public const string TrialBalanceFile = "trial_balance.csv";

    /// <summary>How many characters one file may hold.</summary>
    public const int MaxFileLength = 2_000_000;

    /// <summary>
    /// The SHA-256 of the cut-over date and the three files, in upper-case hexadecimal: the same files for the same date
    /// have the same hash, whoever submits them and whenever.
    /// </summary>
    public string ContentHash()
    {
        var text = string.Join(
            "\n\u001e\n",
            CutOverDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BillsCsv ?? string.Empty,
            AdvancesCsv ?? string.Empty,
            TrialBalanceCsv ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
