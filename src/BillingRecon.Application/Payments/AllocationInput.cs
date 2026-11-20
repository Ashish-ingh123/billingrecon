using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>One line of an allocation request: an amount of the payment for one bill (BR-303).</summary>
public sealed record AllocationInput(BillId BillId, decimal? Amount);
