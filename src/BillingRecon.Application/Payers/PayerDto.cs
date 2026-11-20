using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// A payer as the API returns it. <see cref="PatientId"/> is the patient the payer is, or null for a guarantor.
/// <see cref="Version"/> is the row version in base64, the same value the response carries in its ETag header; a change
/// sends it back in If-Match (BR-804, ADR 0007).
/// </summary>
public sealed record PayerDto(
    Guid Id,
    PayerKind Kind,
    string Name,
    Guid? PatientId,
    string? Phone,
    string? Email,
    PayerStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
