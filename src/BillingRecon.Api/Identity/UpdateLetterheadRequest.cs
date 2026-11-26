using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The hospital's letterhead (ADR 0054): every part is sent, and one left out or blank is cleared. Unknown members are
/// refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLetterheadRequest
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

    /// <summary>The PIN code, six digits.</summary>
    public string? PinCode { get; init; }

    /// <summary>The phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>The e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>The GSTIN, fifteen letters and digits.</summary>
    public string? Gstin { get; init; }

    /// <summary>The line under the place of a signature.</summary>
    public string? Signatory { get; init; }

    /// <summary>The signatory's designation.</summary>
    public string? SignatoryDesignation { get; init; }

    /// <summary>The command, for the version of the settings the client read.</summary>
    public UpdateLetterheadCommand ToCommand(string expectedVersion) => new()
    {
        LegalName = LegalName,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PinCode = PinCode,
        Phone = Phone,
        Email = Email,
        Gstin = Gstin,
        Signatory = Signatory,
        SignatoryDesignation = SignatoryDesignation,
        ExpectedVersion = expectedVersion,
    };
}
