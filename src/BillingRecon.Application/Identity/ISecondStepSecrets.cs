using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The secrets of the second step of a sign-in (ADR 0061): where they are made from the system's random numbers, and
/// how the one of a user is kept unreadable at rest. The use cases never see the key that protects them.
/// </summary>
public interface ISecondStepSecrets
{
    /// <summary>A new secret for an authenticator app: 20 random bytes.</summary>
    public byte[] NewSecret();

    /// <summary>The secret as it is stored: encrypted, and bound to this user of this tenant, so that it opens for no other row.</summary>
    public string Protect(TenantId tenantId, UserId userId, ReadOnlySpan<byte> secret);

    /// <summary>The secret a stored value holds; null when it does not open (another user's value, another key, a changed value).</summary>
    public byte[]? Unprotect(TenantId tenantId, UserId userId, string protectedSecret);

    /// <summary>Ten new recovery codes, each 80 random bits written as <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    public IReadOnlyList<string> NewRecoveryCodes();

    /// <summary>
    /// The hash a recovery code is stored and found by, whatever its case and with or without its dashes. A code has
    /// 80 random bits, so a fast hash is enough: there is no small space of passwords to guess through.
    /// </summary>
    public string HashRecoveryCode(string code);
}
