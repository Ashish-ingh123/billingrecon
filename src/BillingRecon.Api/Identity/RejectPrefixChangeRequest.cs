using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/settings/prefix-changes/{id}/reject</c>: why, which is required. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectPrefixChangeRequest
{
    /// <summary>The reason, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for change <paramref name="id"/>, at the version the client read.</summary>
    public RejectPrefixChangeCommand ToCommand(PrefixChangeId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
