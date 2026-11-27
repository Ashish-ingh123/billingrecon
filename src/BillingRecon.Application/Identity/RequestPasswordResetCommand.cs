
namespace BillingRecon.Application.Identity;

/// <summary>A link for a forgotten password is asked for: the hospital's code and the address, as at the sign-in.</summary>
public sealed record RequestPasswordResetCommand(string Tenant, string Email);
