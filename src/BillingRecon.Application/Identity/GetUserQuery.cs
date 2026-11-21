using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Reads one user.</summary>
public sealed record GetUserQuery(UserId Id);
