
namespace BillingRecon.Application.Identity;

/// <summary>What this deployment offers at the sign-in.</summary>
/// <param name="PasswordResetByMail">True when a forgotten password can be reset by a link in a mail.</param>
public sealed record SignInOptionsDto(bool PasswordResetByMail);
