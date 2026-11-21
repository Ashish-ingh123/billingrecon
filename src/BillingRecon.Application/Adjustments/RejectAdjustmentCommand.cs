using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// A checker rejects an adjustment that waits for approval, saying why; it becomes a draft again (BR-605).
/// <c>ExpectedVersion</c> is the version the checker read (BR-804).
/// </summary>
public sealed record RejectAdjustmentCommand(AdjustmentId Id, string ExpectedVersion, string Comment);
