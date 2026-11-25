namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin sets the hospital's letterhead (ADR 0054). Every part is sent; one left out or blank is cleared.
/// </summary>
public sealed record UpdateLetterheadCommand
{
    /// <summary>The legal name, when it differs from the hospital's name.</summary>
    public string? LegalName { get; init; }

    /// <summary>The first line of the address.</summary>
    public string? AddressLine1 { get; init; }

    /// <summary>The second line of the address.</summary>
    public string? AddressLine2 { get; init; }

    /// <summary>The city.</summary>
    public string? City { get; init; }

    /// <summary>The state: the place of supply of every document.</summary>
    public string? State { get; init; }

    /// <summary>The PIN code: six digits.</summary>
    public string? PinCode { get; init; }

    /// <summary>The phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>The e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>The GSTIN: fifteen capital letters and digits.</summary>
    public string? Gstin { get; init; }

    /// <summary>The line under the place of a signature.</summary>
    public string? Signatory { get; init; }

    /// <summary>The version of the settings the client read.</summary>
    public required string ExpectedVersion { get; init; }
}
