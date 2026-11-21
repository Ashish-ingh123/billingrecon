using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Reads one adjustment.</summary>
public sealed record GetAdjustmentQuery(AdjustmentId Id);
