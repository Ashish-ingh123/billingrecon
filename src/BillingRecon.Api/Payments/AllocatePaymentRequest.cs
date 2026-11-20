using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The body of <c>POST /api/v1/payments/{id}/allocations</c> (BR-303): the bills and the amount for each. Unknown
/// members are refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllocatePaymentRequest
{
    /// <summary>Between 1 and 50 allocations; a bill is named once.</summary>
    public IReadOnlyList<AllocationRequest?>? Allocations { get; init; }

    /// <summary>
    /// The command for payment <paramref name="id"/>, at the version the client read. A line left null stays null and
    /// a bill left out becomes the empty id; the validator refuses both.
    /// </summary>
    public AllocatePaymentCommand ToCommand(PaymentId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Allocations = [.. (Allocations ?? []).Select(line => line is null ? null! : new AllocationInput(new BillId(line.BillId ?? Guid.Empty), line.Amount))],
    };
}
