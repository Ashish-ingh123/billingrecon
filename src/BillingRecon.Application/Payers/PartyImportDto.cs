using BillingRecon.Application.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// What an import of patients and guarantors did, or with a dry run would do (ADR 0053). <see cref="Written"/> is true
/// only when it was no dry run and no row was rejected: one rejected row writes nothing at all.
/// </summary>
/// <param name="DryRun">Whether the files were only checked.</param>
/// <param name="Written">Whether the rows were written.</param>
/// <param name="PatientsCreated">Patients the tenant did not have, each made with its self-pay payer.</param>
/// <param name="PatientsMatched">Patients the tenant has already under the same MRN and name: left as they are.</param>
/// <param name="PayersCreated">Self-pay payers made: one per new patient, and one per matched patient that had none.</param>
/// <param name="GuarantorsCreated">Guarantors the tenant did not have.</param>
/// <param name="GuarantorsMatched">Guarantors the tenant has already under the same reference: left as they are.</param>
/// <param name="Rejected">Every row that cannot be brought in, with its file, line and reason.</param>
public sealed record PartyImportDto(
    bool DryRun,
    bool Written,
    int PatientsCreated,
    int PatientsMatched,
    int PayersCreated,
    int GuarantorsCreated,
    int GuarantorsMatched,
    IReadOnlyList<ImportFinding> Rejected);
