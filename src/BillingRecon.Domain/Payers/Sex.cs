namespace BillingRecon.Domain.Payers;

/// <summary>
/// The sex recorded for a patient (docs/database.md, section 3.2). The member names are stored as text, so renaming one
/// is a data migration.
/// </summary>
public enum Sex
{
    /// <summary>Male.</summary>
    Male,

    /// <summary>Female.</summary>
    Female,

    /// <summary>Another sex.</summary>
    Other,

    /// <summary>Recorded as not known.</summary>
    Unknown,
}
