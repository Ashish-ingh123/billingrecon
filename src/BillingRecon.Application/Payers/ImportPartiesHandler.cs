using System.Globalization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Brings in patients and guarantors from the system before (ADR 0053; BR-101, BR-104). A patient is found by its MRN:
/// one the tenant has under the same name is matched and left as it is, one it has under another name is rejected and
/// never merged, and a new one is registered with the self-pay payer it is. A guarantor is found by the reference it had
/// before. Every row is checked by the rules a user's own registration meets, and every row that fails is reported with
/// its line and reason; if any fails, or with a dry run, nothing is written. What is written is one commit.
/// </summary>
public sealed class ImportPartiesHandler(
    IValidator<ImportPartiesCommand> validator,
    IValidator<RegisterPatientCommand> patientRules,
    IValidator<CreatePayerCommand> payerRules,
    IImportParties known,
    IPatientRepository patients,
    IPayerRepository payers,
    IPayerAccounts accounts,
    IUnitOfWork unitOfWork) : ICommandHandler<ImportPartiesCommand, PartyImportDto>
{
    private static readonly string[] PatientColumns = ["mrn", "full_name"];
    private static readonly string[] GuarantorColumns = ["guarantor_ref", "name"];

    /// <inheritdoc />
    public async Task<Result<PartyImportDto>> HandleAsync(ImportPartiesCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var findings = new List<ImportFinding>();
        var newPatients = await ReadPatientsAsync(CsvTable.Parse(command.PatientsCsv), findings, ct);
        var newGuarantors = await ReadGuarantorsAsync(CsvTable.Parse(command.GuarantorsCsv), findings, ct);

        var foundPatients = await known.FindPatientsAsync([.. newPatients.Select(row => row.Command.Mrn)], ct);
        var foundGuarantors = await known.FindGuarantorsAsync([.. newGuarantors.Select(row => row.Reference)], ct);

        var toRegister = new List<RegisterPatientCommand>();
        var toLink = new List<(PatientId PatientId, string Name)>();
        var matchedPatients = 0;
        foreach (var (line, row) in newPatients)
        {
            if (!foundPatients.TryGetValue(row.Mrn, out var existing))
            {
                toRegister.Add(row);
                continue;
            }

            if (!string.Equals(existing.FullName, row.FullName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ImportFinding(ImportPartiesCommand.PatientsFile, line, $"MRN {row.Mrn} is a patient here already, under another name. Nothing is merged: correct the file or the patient."));
            }
            else if (!existing.Active)
            {
                findings.Add(new ImportFinding(ImportPartiesCommand.PatientsFile, line, $"MRN {row.Mrn} is a patient here already and is inactive (BR-102). Reactivate the patient or take the row out."));
            }
            else
            {
                matchedPatients++;
                if (existing.PayerId is null)
                {
                    toLink.Add((existing.Id, existing.FullName));
                }
            }
        }

        var toCreate = newGuarantors.Where(row => !foundGuarantors.ContainsKey(row.Reference)).ToList();
        var written = !command.DryRun && findings.Count == 0;
        if (written)
        {
            foreach (var row in toRegister)
            {
                var patient = Patient.Register(Mrn.Of(row.Mrn), row.ToDetails());
                patients.Add(patient);
                AddPayer(new PayerDetails { Name = patient.FullName, Phone = row.Phone, Email = row.Email }, patient.Id, legacyReference: null);
            }

            foreach (var (patientId, name) in toLink)
            {
                AddPayer(new PayerDetails { Name = name }, patientId, legacyReference: null);
            }

            foreach (var row in toCreate)
            {
                AddPayer(row.Command.ToDetails(), patientId: null, row.Reference);
            }

            var committed = await unitOfWork.CommitAsync(ct);
            if (committed.IsFailure)
            {
                return committed.Error;
            }
        }

        return new PartyImportDto(
            DryRun: command.DryRun,
            Written: written,
            PatientsCreated: toRegister.Count,
            PatientsMatched: matchedPatients,
            PayersCreated: toRegister.Count + toLink.Count,
            GuarantorsCreated: toCreate.Count,
            GuarantorsMatched: newGuarantors.Count - toCreate.Count,
            Rejected: [.. findings.OrderBy(finding => finding.File, StringComparer.Ordinal).ThenBy(finding => finding.Line)]);
    }

    private void AddPayer(PayerDetails details, PatientId? patientId, string? legacyReference)
    {
        // Self-pay is the one kind R1 has (BR-103), so the creation cannot be refused.
        var payer = Payer.Create(PayerKind.SelfPay, details, patientId, legacyReference).Value;
        payers.Add(payer);

        // The cached balances of the payer, at zero, in the same commit (BR-404): the ledger moves that row later.
        accounts.Open(payer.Id);
    }

    private async Task<List<(int Line, RegisterPatientCommand Command)>> ReadPatientsAsync(CsvTable table, List<ImportFinding> findings, CancellationToken ct)
    {
        var rows = new List<(int Line, RegisterPatientCommand Command)>();
        if (!CheckFile(table, ImportPartiesCommand.PatientsFile, PatientColumns, findings))
        {
            return rows;
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            var reasons = new List<string>();
            var dateOfBirth = Date(table.Field(row, "date_of_birth"), "date_of_birth", reasons);
            var sex = Sex(table.Field(row, "sex"), reasons);
            var command = new RegisterPatientCommand
            {
                Mrn = table.Field(row, "mrn") ?? string.Empty,
                FullName = table.Field(row, "full_name") ?? string.Empty,
                DateOfBirth = dateOfBirth,
                Sex = sex,
                Phone = table.Field(row, "phone"),
                Email = table.Field(row, "email"),
                AddressLine1 = table.Field(row, "address_line1"),
                AddressLine2 = table.Field(row, "address_line2"),
                City = table.Field(row, "city"),
                State = table.Field(row, "state"),
                PinCode = table.Field(row, "pin_code"),
            };

            reasons.AddRange(Messages(await patientRules.GetErrorAsync(command, ct)));
            if (reasons.Count == 0 && !seen.TryAdd(command.Mrn, row.Line))
            {
                reasons.Add($"MRN {command.Mrn} is on line {seen[command.Mrn]} too (BR-101). A patient is in the file once.");
            }

            if (reasons.Count == 0)
            {
                rows.Add((row.Line, command));
            }
            else
            {
                findings.Add(new ImportFinding(ImportPartiesCommand.PatientsFile, row.Line, string.Join(' ', reasons)));
            }
        }

        return rows;
    }

    private async Task<List<(int Line, string Reference, CreatePayerCommand Command)>> ReadGuarantorsAsync(CsvTable table, List<ImportFinding> findings, CancellationToken ct)
    {
        var rows = new List<(int Line, string Reference, CreatePayerCommand Command)>();
        if (table.Headers.Count == 0 || !CheckFile(table, ImportPartiesCommand.GuarantorsFile, GuarantorColumns, findings))
        {
            return rows;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var reasons = new List<string>();
            var reference = table.Field(row, "guarantor_ref");
            if (reference is null || reference.Length > Payer.LegacyReferenceMaxLength)
            {
                reasons.Add($"The guarantor_ref is required and has at most {Payer.LegacyReferenceMaxLength} characters.");
            }

            var command = new CreatePayerCommand
            {
                Kind = PayerKind.SelfPay,
                Name = table.Field(row, "name") ?? string.Empty,
                Phone = table.Field(row, "phone"),
                Email = table.Field(row, "email"),
            };

            reasons.AddRange(Messages(await payerRules.GetErrorAsync(command, ct)));
            if (reasons.Count == 0 && !seen.TryAdd(reference!, row.Line))
            {
                reasons.Add($"The guarantor_ref {reference} is on line {seen[reference!]} too. A guarantor is in the file once.");
            }

            if (reasons.Count == 0)
            {
                rows.Add((row.Line, reference!, command));
            }
            else
            {
                findings.Add(new ImportFinding(ImportPartiesCommand.GuarantorsFile, row.Line, string.Join(' ', reasons)));
            }
        }

        return rows;
    }

    private static bool CheckFile(CsvTable table, string file, string[] required, List<ImportFinding> findings)
    {
        var missing = table.Missing(required);
        if (missing.Count > 0)
        {
            findings.Add(new ImportFinding(file, 1, $"The header lacks: {string.Join(", ", missing)}."));
            return false;
        }

        if (table.Rows.Count > ImportPartiesCommand.MaxRows)
        {
            findings.Add(new ImportFinding(file, 0, $"The file has {table.Rows.Count} rows; one import takes at most {ImportPartiesCommand.MaxRows}. Split it."));
            return false;
        }

        return true;
    }

    private static DateOnly? Date(string? text, string column, List<string> reasons)
    {
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        reasons.Add($"The {column} is written yyyy-MM-dd.");
        return null;
    }

    private static Sex? Sex(string? text, List<string> reasons)
    {
        if (text is null)
        {
            return null;
        }

        if (Enum.TryParse<Sex>(text, ignoreCase: true, out var sex) && Enum.IsDefined(sex) && !int.TryParse(text, CultureInfo.InvariantCulture, out _))
        {
            return sex;
        }

        reasons.Add($"The sex is one of {string.Join(", ", Enum.GetNames<Sex>())}, or left out.");
        return null;
    }

    private static IEnumerable<string> Messages(Error? error)
        => error is null ? [] : error.FieldErrors.Values.SelectMany(messages => messages);
}
