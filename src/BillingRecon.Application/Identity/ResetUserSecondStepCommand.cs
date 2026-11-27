using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>An administrator takes the second step of a user away.</summary>
public sealed record ResetUserSecondStepCommand(UserId Id, string ExpectedVersion);
