using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Replaces the date, the kind, the amount and the reason of a draft adjustment. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record UpdateAdjustmentCommand(AdjustmentId Id, string ExpectedVersion, DateOnly? AdjustmentDate, AdjustmentKind? Kind, decimal? Amount, string Reason);
