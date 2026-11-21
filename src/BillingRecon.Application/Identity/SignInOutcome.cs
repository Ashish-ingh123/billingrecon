using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The result of a sign-in that was well-formed: either the token, or why it was refused and, when the address was
/// known, whose it was. The refusal is for the log; the API answers every refusal the same way.
/// </summary>
public sealed record SignInOutcome(SignedInDto? SignedIn, SignInRefusal? Refusal, UserId? UserId)
{
    /// <summary>A sign-in that succeeded.</summary>
    public static SignInOutcome Success(SignedInDto signedIn, UserId userId) => new(signedIn, null, userId);

    /// <summary>A sign-in that was refused for <paramref name="refusal"/>.</summary>
    public static SignInOutcome Refused(SignInRefusal refusal, UserId? userId = null) => new(null, refusal, userId);
}
