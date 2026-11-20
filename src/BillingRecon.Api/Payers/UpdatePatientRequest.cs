using System.Text.Json.Serialization;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The body of <c>PATCH /api/v1/patients/{id}</c>: the complete demographics, every member sent, and an optional one
/// left out is cleared. The MRN and the status cannot be changed here: a body that names them, or any member the API
/// does not know, is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePatientRequest
{
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

    /// <summary>The command for patient <paramref name="id"/>, at the version the client read.</summary>
    public UpdatePatientCommand ToCommand(PatientId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
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
