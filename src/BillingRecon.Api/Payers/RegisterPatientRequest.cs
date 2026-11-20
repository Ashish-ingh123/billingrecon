using System.Text.Json.Serialization;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The body of <c>POST /api/v1/patients</c>. Every member may be missing from the JSON; the validator of the use case
/// says which are required. A member the API does not know is refused with 400, so a misspelt field cannot pass
/// unnoticed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterPatientRequest
{
    /// <summary>The MRN from the hospital's HIS (BR-101).</summary>
    public string? Mrn { get; init; }

    /// <summary>The full name.</summary>
    public string? FullName { get; init; }

    /// <summary>The date of birth, as <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>The recorded sex: Male, Female, Other or Unknown.</summary>
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

    /// <summary>The PIN code.</summary>
    public string? PinCode { get; init; }

    /// <summary>The command; a missing required text becomes empty, which the validator reports.</summary>
    public RegisterPatientCommand ToCommand() => new()
    {
        Mrn = Mrn ?? string.Empty,
        FullName = FullName ?? string.Empty,
        DateOfBirth = DateOfBirth,
        Sex = Sex,
        Phone = Phone,
        Email = Email,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PinCode = PinCode,
    };
}
