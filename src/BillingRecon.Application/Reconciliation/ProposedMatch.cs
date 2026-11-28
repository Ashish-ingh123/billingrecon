namespace BillingRecon.Application.Reconciliation;

/// <summary>A proposal the matcher makes for a line: its rule (1 to 3), its score, its items and the reason in words.</summary>
public sealed record ProposedMatch(int Rule, int Score, IReadOnlyList<MatchCandidate> Items, string Reason);
