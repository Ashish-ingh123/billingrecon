using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Enables a disabled user, under the version the client read.</summary>
public sealed record EnableUserCommand(UserId Id, string ExpectedVersion);
