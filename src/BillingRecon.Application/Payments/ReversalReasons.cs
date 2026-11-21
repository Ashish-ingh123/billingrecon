using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Where the reason of a reversal comes from when the checker approves it.</summary>
internal static class ReversalReasons
{
    /// <summary>
    /// The reason the maker gave, which the request kept as its comment (ADR 0027): it is copied to the row only when
    /// the reversal happens.
    /// </summary>
    /// <exception cref="InvalidOperationException">The request has no comment: it was not opened by a reversal use case.</exception>
    public static string Of(ApprovalRequest request)
        => request.MakerComment ?? throw new InvalidOperationException($"Approval request {request.Id} asks for a reversal and carries no reason.");
}
