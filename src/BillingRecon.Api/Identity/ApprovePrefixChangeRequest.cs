using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Identity;

/// <summary>The optional body of <c>POST /api/v1/settings/prefix-changes/{id}/approve</c>: a comment. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovePrefixChangeRequest
{
    /// <summary>At most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for change <paramref name="id"/>, at the version the client read.</summary>
    public ApprovePrefixChangeCommand ToCommand(PrefixChangeId id, string expectedVersion) => new(id, expectedVersion, Comment);
}
