using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Disables a user, under the version the client read.</summary>
public sealed record DisableUserCommand(UserId Id, string ExpectedVersion);
