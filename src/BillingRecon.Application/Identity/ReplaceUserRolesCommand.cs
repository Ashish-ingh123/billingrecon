using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Replaces the roles of a user, under the version the client read.</summary>
public sealed record ReplaceUserRolesCommand(UserId Id, string ExpectedVersion, IReadOnlyList<Role> Roles);
