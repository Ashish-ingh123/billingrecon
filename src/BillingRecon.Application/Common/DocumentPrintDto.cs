namespace BillingRecon.Application.Common;

/// <summary>
/// One recorded print of a document (ADR 0054): the how-manieth it is, whether that makes it the original, and when. A
/// printed page shows "Original" for the first and "Duplicate" for every later one.
/// </summary>
public sealed record DocumentPrintDto(int CopyNumber, bool Original, DateTimeOffset PrintedAtUtc);
