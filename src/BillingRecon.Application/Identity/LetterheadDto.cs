namespace BillingRecon.Application.Identity;

/// <summary>
/// What the hospital prints at the head and foot of its documents (ADR 0054). <paramref name="Name"/> is the name the
/// hospital is known by, which is printed when it has given no legal name; every other part is null until it is set.
/// </summary>
public sealed record LetterheadDto(
    string Name,
    string? LegalName,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PinCode,
    string? Phone,
    string? Email,
    string? Gstin,
    string? Signatory);
