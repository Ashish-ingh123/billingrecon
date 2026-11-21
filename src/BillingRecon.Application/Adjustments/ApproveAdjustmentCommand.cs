using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// A checker approves an adjustment that waits for approval, which posts it (BR-606). <c>ExpectedVersion</c> is the
/// version the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApproveAdjustmentCommand(AdjustmentId Id, string ExpectedVersion, string? Comment);
