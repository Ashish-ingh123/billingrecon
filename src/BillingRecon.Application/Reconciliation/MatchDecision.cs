namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What a matching run does with a line: confirm it with its items and the reason, propose one or more matches, or
/// leave it for a person (BR-1010, BR-1011).
/// </summary>
public sealed record MatchDecision(IReadOnlyList<MatchCandidate>? Confirmed, string? Reason, IReadOnlyList<ProposedMatch> Proposals)
{
    /// <summary>Nothing fits the line.</summary>
    public static MatchDecision Nothing { get; } = new(null, null, []);

    /// <summary>The certain case: the line is matched to <paramref name="items"/>.</summary>
    public static MatchDecision Confirm(IReadOnlyList<MatchCandidate> items, string reason) => new(items, reason, []);

    /// <summary>The line is offered <paramref name="proposals"/>, the likeliest first.</summary>
    public static MatchDecision ProposeAll(IReadOnlyList<ProposedMatch> proposals)
        => new(null, null, [.. proposals.OrderByDescending(proposal => proposal.Score)]);
}
