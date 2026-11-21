using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The maker asks for a draft adjustment to be posted, which hands it to a second person (BR-502, BR-604).
/// <c>ExpectedVersion</c> is the version the client read (BR-804).
/// </summary>
public sealed record PostAdjustmentCommand(AdjustmentId Id, string ExpectedVersion);
