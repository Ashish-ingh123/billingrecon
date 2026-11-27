using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The tokens of the links (ADR 0061, amended): 32 random bytes behind the id of their user, so that a link finds its
/// user without a search across hospitals, and only the hash is ever stored.
/// </summary>
public interface IPasswordResetTokens
{
    /// <summary>A new token for <paramref name="userId"/> and the hash it is stored by.</summary>
    public (string Token, string Hash) New(UserId userId);

    /// <summary>The user a token names and the hash it is found by; false for what is no token.</summary>
    public bool TryRead(string? token, out UserId userId, out string hash);
}
