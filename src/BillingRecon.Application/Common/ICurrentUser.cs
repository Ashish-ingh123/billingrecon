using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Common;

/// <summary>The user the current unit of work acts for (BR-602). In a request it comes from the validated token.</summary>
public interface ICurrentUser
{
    /// <summary>
    /// The acting user. Null when no user is acting: system work (seeding, migrations, background jobs). Rows
    /// written then carry no user in their audit columns (ADR 0015).
    /// </summary>
    public UserId? UserId { get; }

    /// <summary>
    /// The roles of the acting user, as its token carries them (the user's roles when they signed in); none when no
    /// user is acting. What a role may call is decided by the API's policies; the one business rule about roles that a
    /// use case asks is the checker's (BR-603, ADR 0032).
    /// </summary>
    public IReadOnlyCollection<Role> Roles { get; }
}
