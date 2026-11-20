using System.Text.Json.Serialization;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The body of <c>POST /api/v1/payers</c>. A member the API does not know is refused with 400, so a misspelt field
/// cannot pass unnoticed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePayerRequest
{
    /// <summary>SelfPay. Insurer and Corporate are reserved for R4 and refused with 422 (BR-103).</summary>
    public PayerKind? Kind { get; init; }

    /// <summary>The patient the payer is; left out for a guarantor (BR-104).</summary>
    public Guid? PatientId { get; init; }

    /// <summary>The name of the payer.</summary>
    public string? Name { get; init; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>The command; a missing name becomes empty, which the validator reports.</summary>
    public CreatePayerCommand ToCommand() => new()
    {
        Kind = Kind,
        PatientId = PatientId is { } patientId ? new PatientId(patientId) : null,
        Name = Name ?? string.Empty,
        Phone = Phone,
        Email = Email,
    };
}
