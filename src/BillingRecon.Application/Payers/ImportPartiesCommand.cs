namespace BillingRecon.Application.Payers;

/// <summary>
/// Brings in the patients of the system before, each with the self-pay payer it is, and its guarantors (ADR 0053), from
/// two CSV files. With <see cref="DryRun"/> nothing is written and the answer says what would be.
/// </summary>
public sealed record ImportPartiesCommand
{
    /// <summary>The name a finding gives the patients' file.</summary>
    public const string PatientsFile = "patients.csv";

    /// <summary>The name a finding gives the guarantors' file.</summary>
    public const string GuarantorsFile = "guarantors.csv";

    /// <summary>How many rows one file may hold: one commit writes them all.</summary>
    public const int MaxRows = 5000;

    /// <summary>How many characters one file may hold.</summary>
    public const int MaxFileLength = 4_000_000;

    /// <summary>
    /// The patients: <c>mrn</c> and <c>full_name</c>, and optionally <c>date_of_birth</c> (yyyy-MM-dd), <c>sex</c>,
    /// <c>phone</c>, <c>email</c>, <c>address_line1</c>, <c>address_line2</c>, <c>city</c>, <c>state</c>, <c>pin_code</c>.
    /// </summary>
    public string? PatientsCsv { get; init; }

    /// <summary>The guarantors, who pay for others: <c>guarantor_ref</c> and <c>name</c>, and optionally <c>phone</c>, <c>email</c>.</summary>
    public string? GuarantorsCsv { get; init; }

    /// <summary>True to check the files and write nothing.</summary>
    public bool DryRun { get; init; }
}
