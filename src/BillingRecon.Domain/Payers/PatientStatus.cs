namespace BillingRecon.Domain.Payers;

/// <summary>
/// Whether a patient can be billed (BR-102). The member names are stored as text (docs/database.md, section 3.2), so
/// renaming one is a data migration.
/// </summary>
public enum PatientStatus
{
    /// <summary>New bills may be raised for the patient.</summary>
    Active,

    /// <summary>No new bill may be raised for the patient; existing bills are unaffected.</summary>
    Inactive,
}
