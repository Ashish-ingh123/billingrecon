namespace BillingRecon.Domain.Identity;

/// <summary>Whether a user may sign in (BR-601). The member names are stored as text.</summary>
public enum UserStatus
{
    /// <summary>May sign in and work.</summary>
    Active = 0,

    /// <summary>Cannot sign in (BR-601). The row stays: nothing that names the user loses its meaning.</summary>
    Disabled = 1,
}
