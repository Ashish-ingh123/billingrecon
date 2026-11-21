namespace BillingRecon.Application.Identity;

/// <summary>
/// Why a sign-in was refused, for the log only: the client is told the same thing whatever the reason (ADR 0031,
/// point 4).
/// </summary>
public enum SignInRefusal
{
    /// <summary>No tenant has the code.</summary>
    UnknownTenant = 0,

    /// <summary>The tenant is suspended.</summary>
    TenantSuspended = 1,

    /// <summary>No user of the tenant has the address.</summary>
    UnknownUser = 2,

    /// <summary>The password is not the user's.</summary>
    WrongPassword = 3,

    /// <summary>The password is right, but the user is disabled (BR-601).</summary>
    UserDisabled = 4,
}
