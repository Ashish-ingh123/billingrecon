using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Voids a draft adjustment, with the reason. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record VoidAdjustmentCommand(AdjustmentId Id, string ExpectedVersion, string Reason);
