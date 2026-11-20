using System.Text.Json.Serialization;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The body of <c>PATCH /api/v1/payers/{id}</c>: the name and every contact detail, and a contact detail left out is
/// cleared. The kind, the patient link and the status cannot be changed here: a body that names them, or any member the
/// API does not know, is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePayerRequest
{
    /// <summary>The name of the payer.</summary>
    public string? Name { get; init; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>The command for payer <paramref name="id"/>, at the version the client read.</summary>
    public UpdatePayerCommand ToCommand(PayerId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Name = Name ?? string.Empty,
        Phone = Phone,
        Email = Email,
    };
}
