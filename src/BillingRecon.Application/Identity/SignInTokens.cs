using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The answer of a sign-in, of a changed password and of a second step: the token the user is given now, and what it
/// is good for (ADR 0031, ADR 0051, ADR 0061). One place decides it, so the three never disagree:
/// a password an administrator set is changed first; then a user who has a second step gives its code; then a user
/// whose roles need one and who has none enrols; and only then is the token a full one.
/// </summary>
public static class SignInTokens
{
    /// <summary>The token and its answer for <paramref name="user"/> as they stand now.</summary>
    /// <param name="tokens">The issuer.</param>
    /// <param name="policy">Who must have a second step.</param>
    /// <param name="clock">The application's clock, for the seconds the token lives.</param>
    /// <param name="tenantId">The tenant of the user.</param>
    /// <param name="user">The user, as saved.</param>
    /// <param name="secondStepGiven">True when this request has just verified the user's second step, or enrolled it.</param>
    public static SignedInDto For(ITokenIssuer tokens, ISecondStepPolicy policy, IClock clock, TenantId tenantId, User user, bool secondStepGiven)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(user);

        Role[] roles = [.. user.Roles.Select(role => role.Role).Order()];
        var stage = user.PasswordChangeDue
            ? SecondStepStage.None
            : secondStepGiven
                ? SecondStepStage.Done
                : user.SecondStepEnrolled
                    ? SecondStepStage.Pending
                    : policy.IsRequiredFor(roles) ? SecondStepStage.Enrolment : SecondStepStage.None;

        var token = tokens.Issue(tenantId, user.Id, roles, user.PasswordChangeDue, user.SessionVersion, stage);
        var seconds = (int)Math.Round((token.ExpiresAtUtc - clock.UtcNow).TotalSeconds, MidpointRounding.AwayFromZero);

        return new SignedInDto(token.Value, SignInHandler.TokenType, token.ExpiresAtUtc, seconds, user.PasswordChangeDue, stage);
    }
}
