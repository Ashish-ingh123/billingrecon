namespace BillingRecon.Application.Payers;

/// <summary>
/// What Payers answers when records of the system before are brought in (ADR 0053): which patients the tenant has under
/// the MRNs of a file, with the payer each is, and which guarantors under the references of a file. The import of
/// patients and the opening batch both ask, so both find a patient the same way: by the MRN, without regard to case.
/// </summary>
public interface IImportParties
{
    /// <summary>The tenant's patients among <paramref name="mrns"/>, by MRN without regard to case.</summary>
    public Task<IReadOnlyDictionary<string, KnownPatient>> FindPatientsAsync(IReadOnlyCollection<string> mrns, CancellationToken ct);

    /// <summary>The tenant's guarantors among <paramref name="references"/>, by the reference they had in the system before, as written.</summary>
    public Task<IReadOnlyDictionary<string, KnownGuarantor>> FindGuarantorsAsync(IReadOnlyCollection<string> references, CancellationToken ct);
}
