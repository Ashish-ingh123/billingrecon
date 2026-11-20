using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Replaces the demographics of a patient: every field is sent, and an optional one left out is cleared. The MRN and
/// the status are not part of it. <see cref="ExpectedVersion"/> is the version the client read (BR-804).
/// </summary>
public sealed record UpdatePatientCommand : IPatientDetailsInput
{
    /// <summary>The patient to correct.</summary>
    public required PatientId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <inheritdoc />
    public required string FullName { get; init; }

    /// <inheritdoc />
    public DateOnly? DateOfBirth { get; init; }

    /// <inheritdoc />
    public Sex? Sex { get; init; }

    /// <inheritdoc />
    public string? Phone { get; init; }

    /// <inheritdoc />
    public string? Email { get; init; }

    /// <inheritdoc />
    public string? AddressLine1 { get; init; }

    /// <inheritdoc />
    public string? AddressLine2 { get; init; }

    /// <inheritdoc />
    public string? City { get; init; }

    /// <inheritdoc />
    public string? State { get; init; }

    /// <inheritdoc />
    public string? PinCode { get; init; }
}
