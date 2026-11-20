using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// A patient as the API returns it. <see cref="Version"/> is the row version in base64, the same value the response
/// carries in its ETag header; a change sends it back in If-Match (BR-804, ADR 0007).
/// </summary>
public sealed record PatientDto(
    Guid Id,
    string Mrn,
    string FullName,
    DateOnly? DateOfBirth,
    Sex? Sex,
    string? Phone,
    string? Email,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PinCode,
    PatientStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
