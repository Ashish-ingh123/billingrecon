namespace BillingRecon.Application.Payers;

/// <summary>
/// The name and contact details a command about a payer carries, as the client sent them: creating a payer and
/// correcting one share them, and share their validator. A missing required text arrives as an empty string.
/// </summary>
public interface IPayerDetailsInput
{
    /// <summary>The name of the payer.</summary>
    public string Name { get; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; }
}
