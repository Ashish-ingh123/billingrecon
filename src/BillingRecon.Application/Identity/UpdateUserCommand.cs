using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Changes the e-mail address and the display name of a user, under the version the client read.</summary>
public sealed record UpdateUserCommand(UserId Id, string ExpectedVersion, string Email, string DisplayName);
