using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>An administrator sets a new password for a user, under the version the client read.</summary>
public sealed record ResetUserPasswordCommand(UserId Id, string ExpectedVersion, string Password);
