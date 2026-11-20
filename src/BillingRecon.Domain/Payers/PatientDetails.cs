namespace BillingRecon.Domain.Payers;

/// <summary>
/// The demographics of a patient that a clerk enters and corrects (docs/database.md, section 3.2): everything but the
/// MRN, which never changes, and the status, which changes by deactivation. An optional value may be null or blank;
/// <see cref="Patient"/> stores it without surrounding white space, and a blank one as null. The limits are those of
/// <see cref="Patient"/>; the validator of the use case checks them first, so a value beyond them is a bug.
/// </summary>
public sealed record PatientDetails
{
    /// <summary>The patient's full name, as the hospital records it.</summary>
    public required string FullName { get; init; }

    /// <summary>The date of birth, if known.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>The recorded sex, if any.</summary>
    public Sex? Sex { get; init; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>The first line of the address.</summary>
    public string? AddressLine1 { get; init; }

    /// <summary>The second line of the address.</summary>
    public string? AddressLine2 { get; init; }

    /// <summary>The city.</summary>
    public string? City { get; init; }

    /// <summary>The state.</summary>
    public string? State { get; init; }

    /// <summary>The postal code: the PIN code for an address in India.</summary>
    public string? PinCode { get; init; }
}
