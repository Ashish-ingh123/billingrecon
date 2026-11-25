namespace BillingRecon.Application.Common;

/// <summary>
/// Why a row of an imported file cannot be brought in (ADR 0053): the file, the line the row starts on (1 for the header,
/// 0 for the file as a whole), and the reason in words a person can act on. A finding never repeats a patient's name,
/// phone or address: it names the row, and the row is in the file.
/// </summary>
public sealed record ImportFinding(string File, int Line, string Reason);
