using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>What a sign-in knows of the user it names: its id, its status, the hash to check the password against, and its roles.</summary>
public sealed record LoginUser(UserId Id, UserStatus Status, string PasswordHash, IReadOnlyList<Role> Roles);
