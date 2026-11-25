using System.Text.Json.Serialization;
using BillingRecon.Application.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The patients and guarantors of the system before (ADR 0053), each file as the text of a CSV file. With
/// <see cref="DryRun"/> nothing is written. Unknown members are refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportPartiesRequest
{
    /// <summary>The patients: mrn and full_name, and optionally date_of_birth, sex, phone, email, address_line1, address_line2, city, state, pin_code.</summary>
    public string? PatientsCsv { get; init; }

    /// <summary>The guarantors: guarantor_ref and name, and optionally phone and email.</summary>
    public string? GuarantorsCsv { get; init; }

    /// <summary>True to check the files and write nothing. Left out, the rows are written when none is rejected.</summary>
    public bool? DryRun { get; init; }

    /// <summary>The import of these files.</summary>
    public ImportPartiesCommand ToCommand() => new() { PatientsCsv = PatientsCsv, GuarantorsCsv = GuarantorsCsv, DryRun = DryRun ?? false };
}
