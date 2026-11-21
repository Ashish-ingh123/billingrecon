using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>The roles of a user were replaced; <c>Roles</c> lists them, comma-separated, in the order of the enum.</summary>
public sealed record UserRolesReplaced(UserId UserId, string Roles) : IDomainEvent;
