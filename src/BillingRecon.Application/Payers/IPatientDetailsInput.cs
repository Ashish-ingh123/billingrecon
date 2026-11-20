using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// The demographics a command about a patient carries, as the client sent them: registering a patient and correcting
/// one share them, and share their validator. A missing required text arrives as an empty string.
/// </summary>
public interface IPatientDetailsInput
{
    /// <summary>The full name.</summary>
    public string FullName { get; }

    /// <summary>The date of birth, if known.</summary>
    public DateOnly? DateOfBirth { get; }

    /// <summary>The recorded sex, if any.</summary>
    public Sex? Sex { get; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; }

    /// <summary>The first line of the address.</summary>
    public string? AddressLine1 { get; }

    /// <summary>The second line of the address.</summary>
    public string? AddressLine2 { get; }

    /// <summary>The city.</summary>
    public string? City { get; }

    /// <summary>The state.</summary>
    public string? State { get; }

    /// <summary>The postal code.</summary>
    public string? PinCode { get; }
}
