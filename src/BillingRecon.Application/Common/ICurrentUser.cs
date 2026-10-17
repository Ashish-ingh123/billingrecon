using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>The user the current unit of work acts for (BR-602). In a request it comes from the validated token.</summary>
public interface ICurrentUser
{
    /// <summary>
    /// The acting user. Null when no user is acting: system work (seeding, migrations, background jobs). Rows
    /// written then carry no user in their audit columns (ADR 0015).
    /// </summary>
    public UserId? UserId { get; }
}
