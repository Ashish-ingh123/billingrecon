namespace BillingRecon.Domain.Payers;

/// <summary>
/// What a clerk enters and corrects about a payer (docs/database.md, section 3.2): its name and contact details. The
/// kind and the patient link are fixed when the payer is created, and the status changes by deactivation. An optional
/// value may be null or blank; <see cref="Payer"/> stores it without surrounding white space, and a blank one as null.
/// </summary>
public sealed record PayerDetails
{
    /// <summary>The name of the payer: the patient's own name, or the guarantor's.</summary>
    public required string Name { get; init; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; init; }
}
