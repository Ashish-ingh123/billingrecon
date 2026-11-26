namespace BillingRecon.Application.Identity;

/// <summary>An image of the letterhead as it is read (ADR 0059): its media type, its bytes and their SHA-256.</summary>
public sealed record LetterheadImageDto(string ContentType, byte[] Content, string Sha256);
