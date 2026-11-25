using System.Text.Json.Serialization;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Receivables;

/// <summary>The decision on an opening batch: a comment, optional for an approval and required for a rejection.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideOpeningBatchRequest
{
    /// <summary>What the checker says.</summary>
    public string? Comment { get; init; }

    /// <summary>The approval of batch <paramref name="id"/> as read at <paramref name="expectedVersion"/>.</summary>
    public ApproveOpeningBatchCommand ToApproval(OpeningBatchId id, string expectedVersion) => new(id, expectedVersion, Comment);

    /// <summary>The rejection of batch <paramref name="id"/> as read at <paramref name="expectedVersion"/>.</summary>
    public RejectOpeningBatchCommand ToRejection(OpeningBatchId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
