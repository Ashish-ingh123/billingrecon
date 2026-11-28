namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What a matching run did: the unmatched lines it read, how many it matched by itself, how many it proposed matches for,
/// how many it left for a person, and how many another run or a user changed first (BR-1029).
/// </summary>
public sealed record MatchingRunDto(int LinesRead, int Matched, int Proposed, int Left, int Skipped);
