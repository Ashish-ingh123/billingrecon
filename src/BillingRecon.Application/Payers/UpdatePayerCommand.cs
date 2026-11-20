using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Replaces the name and contact details of a payer: every field is sent, and a contact detail left out is cleared. The
/// kind, the patient link and the status are not part of it. <see cref="ExpectedVersion"/> is the version the client
/// read (BR-804).
/// </summary>
public sealed record UpdatePayerCommand : IPayerDetailsInput
{
    /// <summary>The payer to correct.</summary>
    public required PayerId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? Phone { get; init; }

    /// <inheritdoc />
    public string? Email { get; init; }
}
