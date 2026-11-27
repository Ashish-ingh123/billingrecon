using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>The body of the approval and of the rejection of the closing of a shift: what the checker has to say.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideCashierShiftRequest
{
    /// <summary>The comment: optional for an approval, required for a rejection.</summary>
    public string? Comment { get; init; }

    /// <summary>The command of the approval.</summary>
    public ApproveCashierShiftClosingCommand ToApproval(CashierShiftId id, string expectedVersion) => new(id, expectedVersion, Comment);

    /// <summary>The command of the rejection.</summary>
    public RejectCashierShiftClosingCommand ToRejection(CashierShiftId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
