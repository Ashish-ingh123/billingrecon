using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>Registers a patient with the MRN from the hospital's HIS and their demographics (US-10, BR-101).</summary>
public sealed record RegisterPatientCommand : IPatientDetailsInput
{
    /// <summary>The MRN as the client sent it; empty when it sent none.</summary>
    public required string Mrn { get; init; }

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
