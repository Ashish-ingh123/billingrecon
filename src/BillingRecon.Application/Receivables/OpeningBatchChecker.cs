using System.Globalization;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// Checks the files of an opening batch against the tenant as it is now (ADR 0053), and writes nothing. The dry run, the
/// submission and the approval all check through here, so the three cannot disagree about a row. A row is rejected, with
/// its file, line and reason, when a value is missing or malformed, its patient or guarantor is not here or is inactive,
/// its date is on or after the cut-over, its old number is in the file twice or was brought in before, or the batch is
/// too large; and the three totals must be those of the trial balance, to the paisa.
/// </summary>
public sealed class OpeningBatchChecker(IImportParties parties, IOpeningReceivables openingReceivables, IOpeningAdvances openingAdvances)
{
    private const int LegacyNumberMaxLength = 50;
    private const decimal MaxAmount = 9_999_999_999.99m;

    private static readonly string[] BillColumns = ["legacy_number", "mrn", "bill_date", "outstanding_amount"];
    private static readonly string[] AdvanceColumns = ["legacy_reference", "received_date", "amount", "method"];
    private static readonly string[] TrialBalanceColumns = ["account", "amount"];

    /// <summary>What <paramref name="files"/> hold that can be brought in, and every finding that stops them.</summary>
    public async Task<OpeningBatchCheck> CheckAsync(OpeningBatchFiles files, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);

        var findings = new List<ImportFinding>();
        var bills = Table(files.BillsCsv, OpeningBatchFiles.BillsFile, BillColumns, findings);
        var advances = Table(files.AdvancesCsv, OpeningBatchFiles.AdvancesFile, AdvanceColumns, findings);

        var mrns = Values(bills, "mrn").Concat(Values(advances, "mrn")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var references = Values(bills, "guarantor_ref").Concat(Values(advances, "guarantor_ref")).Distinct(StringComparer.Ordinal).ToArray();
        var patients = await parties.FindPatientsAsync(mrns, ct);
        var guarantors = await parties.FindGuarantorsAsync(references, ct);
        var billsBefore = await openingReceivables.FindBroughtForwardAsync([.. Values(bills, "legacy_number")], ct);
        var advancesBefore = await openingAdvances.FindBroughtForwardAsync([.. Values(advances, "legacy_reference")], ct);

        var receivables = new List<OpeningReceivable>();
        var seenBills = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in bills?.Rows ?? [])
        {
            var reasons = new List<string>();
            var legacy = Legacy(bills!.Field(row, "legacy_number"), "legacy_number", seenBills, billsBefore, row.Line, reasons);
            var patient = Patient(bills.Field(row, "mrn"), patients, required: true, reasons);
            var payer = Payer(patient, bills.Field(row, "guarantor_ref"), guarantors, reasons);
            var billDate = Date(bills.Field(row, "bill_date"), "bill_date", files.CutOverDate, reasons);
            var dueDate = bills.Field(row, "due_date") is { } due ? Date(due, "due_date", cutOver: null, reasons) : billDate;
            if (billDate is { } billed && dueDate is { } owed && owed < billed)
            {
                reasons.Add("The due_date is on or after the bill_date.");
            }

            var amount = Amount(bills.Field(row, "outstanding_amount"), "outstanding_amount", reasons);
            if (reasons.Count == 0)
            {
                receivables.Add(new OpeningReceivable(legacy!, patient!.Id, payer!.Value, billDate!.Value, dueDate!.Value, amount!.Value));
            }
            else
            {
                findings.Add(new ImportFinding(OpeningBatchFiles.BillsFile, row.Line, string.Join(' ', reasons)));
            }
        }

        var broughtAdvances = new List<OpeningAdvance>();
        var seenAdvances = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in advances?.Rows ?? [])
        {
            var reasons = new List<string>();
            var legacy = Legacy(advances!.Field(row, "legacy_reference"), "legacy_reference", seenAdvances, advancesBefore, row.Line, reasons);
            var mrn = advances.Field(row, "mrn");
            var reference = advances.Field(row, "guarantor_ref");
            PayerId? payer = null;
            if (mrn is null == reference is null)
            {
                reasons.Add("Give the mrn or the guarantor_ref of the payer, and not both.");
            }
            else
            {
                payer = Payer(Patient(mrn, patients, required: false, reasons), reference, guarantors, reasons);
            }

            var received = Date(advances.Field(row, "received_date"), "received_date", files.CutOverDate, reasons);
            var amount = Amount(advances.Field(row, "amount"), "amount", reasons);
            var method = Method(advances.Field(row, "method"), reasons);
            var credit = Kind(advances.Field(row, "kind"), reasons);
            if (reasons.Count == 0)
            {
                broughtAdvances.Add(new OpeningAdvance(legacy!, payer!.Value, received!.Value, method!, amount!.Value, credit));
            }
            else
            {
                findings.Add(new ImportFinding(OpeningBatchFiles.AdvancesFile, row.Line, string.Join(' ', reasons)));
            }
        }

        var check = new OpeningBatchCheck(receivables, broughtAdvances, findings);
        var documents = (bills?.Rows.Count ?? 0) + (advances?.Rows.Count ?? 0);
        if (documents == 0)
        {
            findings.Add(new ImportFinding(OpeningBatchFiles.BillsFile, 0, "The files hold no opening balance: there is nothing to bring in."));
        }
        else if (documents > OpeningBatch.MaxDocuments)
        {
            findings.Add(new ImportFinding(OpeningBatchFiles.BillsFile, 0, $"The files hold {documents} opening balances; one batch takes at most {OpeningBatch.MaxDocuments}. Split them, each part with its own trial balance."));
        }

        // The totals are held against the trial balance only when every row could be read: a rejected row is missing
        // from them, and the difference would say nothing new.
        if (findings.Count == 0)
        {
            CheckTrialBalance(files.TrialBalanceCsv, check, findings);
        }
        else
        {
            Table(files.TrialBalanceCsv, OpeningBatchFiles.TrialBalanceFile, TrialBalanceColumns, findings, required: true);
        }

        return check with { Findings = [.. findings.OrderBy(finding => Order(finding.File)).ThenBy(finding => finding.Line)] };
    }

    private static void CheckTrialBalance(string? text, OpeningBatchCheck check, List<ImportFinding> findings)
    {
        var table = Table(text, OpeningBatchFiles.TrialBalanceFile, TrialBalanceColumns, findings, required: true);
        if (table is null)
        {
            return;
        }

        var expected = new Dictionary<string, Money>(StringComparer.OrdinalIgnoreCase)
        {
            ["Receivable"] = check.ReceivableTotal,
            ["Advance"] = check.AdvanceTotal - check.CreditBalanceTotal,
            ["Credit"] = check.CreditBalanceTotal,
        };
        var given = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            var reasons = new List<string>();
            var account = table.Field(row, "account");
            var amount = Amount(table.Field(row, "amount"), "amount", reasons, allowZero: true);
            if (account is null || !expected.TryGetValue(account, out var total))
            {
                reasons.Add("The account is Receivable, Advance or Credit.");
            }
            else if (!given.TryAdd(account, row.Line))
            {
                reasons.Add($"The account {account} is on line {given[account]} too.");
            }
            else if (amount is { } stated && stated != total)
            {
                reasons.Add($"The trial balance says {Text(stated)} for {account}; the rows of the files come to {Text(total)}.");
            }

            if (reasons.Count > 0)
            {
                findings.Add(new ImportFinding(OpeningBatchFiles.TrialBalanceFile, row.Line, string.Join(' ', reasons)));
            }
        }

        foreach (var (account, total) in expected.Where(account => !given.ContainsKey(account.Key) && !account.Value.IsZero))
        {
            findings.Add(new ImportFinding(OpeningBatchFiles.TrialBalanceFile, 0, $"The trial balance has no row for {account}; the rows of the files come to {Text(total)}."));
        }
    }

    // A file that is left out holds no row; one that is there must have its columns.
    private static CsvTable? Table(string? text, string file, string[] columns, List<ImportFinding> findings, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required)
            {
                findings.Add(new ImportFinding(file, 0, "The file is required."));
            }

            return null;
        }

        if (text.Length > OpeningBatchFiles.MaxFileLength)
        {
            findings.Add(new ImportFinding(file, 0, $"The file holds at most {OpeningBatchFiles.MaxFileLength} characters; split the batch."));
            return null;
        }

        var table = CsvTable.Parse(text);
        var missing = table.Missing(columns);
        if (missing.Count > 0)
        {
            findings.Add(new ImportFinding(file, 1, $"The header lacks: {string.Join(", ", missing)}."));
            return null;
        }

        return table;
    }

    private static IEnumerable<string> Values(CsvTable? table, string column)
        => table is null ? [] : table.Rows.Select(row => table.Field(row, column)).OfType<string>();

    private static string? Legacy(string? value, string column, Dictionary<string, int> seen, IReadOnlySet<string> before, int line, List<string> reasons)
    {
        if (value is null || value.Length > LegacyNumberMaxLength)
        {
            reasons.Add($"The {column} is required and has at most {LegacyNumberMaxLength} characters.");
            return null;
        }

        if (!seen.TryAdd(value, line))
        {
            reasons.Add($"The {column} {value} is on line {seen[value]} too.");
        }
        else if (before.Contains(value))
        {
            reasons.Add($"The {column} {value} was brought in before; an opening balance is brought in once.");
        }

        return value;
    }

    private static KnownPatient? Patient(string? mrn, IReadOnlyDictionary<string, KnownPatient> patients, bool required, List<string> reasons)
    {
        if (mrn is null)
        {
            if (required)
            {
                reasons.Add("The mrn is required.");
            }

            return null;
        }

        if (!patients.TryGetValue(mrn, out var patient))
        {
            reasons.Add($"MRN {mrn} is not a patient here: import the patients first.");
            return null;
        }

        if (!patient.Active)
        {
            reasons.Add($"The patient with MRN {mrn} is inactive (BR-102).");
        }

        return patient;
    }

    // Who owes or holds it: the guarantor when one is named, the patient's own payer otherwise.
    private static PayerId? Payer(KnownPatient? patient, string? reference, IReadOnlyDictionary<string, KnownGuarantor> guarantors, List<string> reasons)
    {
        if (reference is not null)
        {
            if (!guarantors.TryGetValue(reference, out var guarantor))
            {
                reasons.Add($"The guarantor_ref {reference} is not a guarantor here: import the guarantors first.");
                return null;
            }

            if (!guarantor.Active)
            {
                reasons.Add($"The guarantor {reference} is inactive (BR-105).");
            }

            return guarantor.Id;
        }

        if (patient is null)
        {
            return null;
        }

        if (patient.PayerId is null)
        {
            reasons.Add("The patient has no self-pay payer here: import the patients again, which makes it.");
        }
        else if (!patient.PayerActive)
        {
            reasons.Add("The payer of the patient is inactive (BR-105).");
        }

        return patient.PayerId;
    }

    private static DateOnly? Date(string? text, string column, DateOnly? cutOver, List<string> reasons)
    {
        if (text is null || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            reasons.Add($"The {column} is required and written yyyy-MM-dd.");
            return null;
        }

        if (cutOver is { } first && date >= first)
        {
            reasons.Add($"The {column} is before the cut-over date {first:yyyy-MM-dd}: an opening balance is from the system before.");
        }

        return date;
    }

    private static Money? Amount(string? text, string column, List<string> reasons, bool allowZero = false)
    {
        if (text is null
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || !Money.TryOf(value, out var amount)
            || value > MaxAmount
            || (allowZero ? amount.IsNegative : !amount.IsPositive))
        {
            reasons.Add($"The {column} is required: a plain number {(allowZero ? "not below" : "above")} zero with at most two decimals, such as 1250.50.");
            return null;
        }

        return amount;
    }

    private string? Method(string? text, List<string> reasons)
    {
        var method = text is null ? null : openingAdvances.Methods.FirstOrDefault(name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            reasons.Add($"The method is one of {string.Join(", ", openingAdvances.Methods)}.");
        }

        return method;
    }

    private static bool Kind(string? text, List<string> reasons)
    {
        if (text is null || string.Equals(text, "Advance", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(text, "Credit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        reasons.Add("The kind is Advance or Credit, or left out for an advance.");
        return false;
    }

    private static string Text(Money amount) => amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static int Order(string file) => file switch
    {
        OpeningBatchFiles.BillsFile => 0,
        OpeningBatchFiles.AdvancesFile => 1,
        _ => 2,
    };
}
