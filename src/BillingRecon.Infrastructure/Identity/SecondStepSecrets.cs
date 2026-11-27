using System.Security.Cryptography;
using System.Text;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Makes and protects the secrets of the second step (ADR 0061), with the BCL alone. A secret is encrypted with
/// AES-256-GCM under <c>TwoStep:EncryptionKey</c>, a fresh nonce each time, and the ids of the tenant and the user as
/// associated data: a value copied to another user's row does not open. The stored text is
/// <c>v1.nonce.ciphertext.tag</c> in base64; the version is there so that a later key or algorithm can be told apart.
/// </summary>
public sealed class SecondStepSecrets(IOptions<TwoStepOptions> options) : ISecondStepSecrets
{
    private const string Version = "v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int RecoveryCodes = 10;
    private const int RecoveryCodeBytes = 10;

    private readonly byte[] _key = options.Value.ReadKey();

    /// <inheritdoc />
    public byte[] NewSecret() => RandomNumberGenerator.GetBytes(Totp.SecretBytes);

    /// <inheritdoc />
    public string Protect(TenantId tenantId, UserId userId, ReadOnlySpan<byte> secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[secret.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, secret, cipher, tag, Bound(tenantId, userId));

        return string.Join('.', Version, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
    }

    /// <inheritdoc />
    public byte[]? Unprotect(TenantId tenantId, UserId userId, string protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);

        var parts = protectedSecret.Split('.');
        if (parts.Length != 4 || parts[0] != Version)
        {
            return null;
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var cipher = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);
            if (nonce.Length != NonceBytes || tag.Length != TagBytes)
            {
                return null;
            }

            var secret = new byte[cipher.Length];
            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, secret, Bound(tenantId, userId));

            return secret;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            // Not a value this key made for this user: the caller decides what that means.
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> NewRecoveryCodes()
        => [.. Enumerable.Range(0, RecoveryCodes).Select(_ =>
        {
            var text = Totp.ToBase32(RandomNumberGenerator.GetBytes(RecoveryCodeBytes));

            return string.Join('-', text[..4], text[4..8], text[8..12], text[12..16]);
        })];

    /// <inheritdoc />
    public string HashRecoveryCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var canonical = new string([.. code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(canonical)));
    }

    private static byte[] Bound(TenantId tenantId, UserId userId) => [.. tenantId.Value.ToByteArray(), .. userId.Value.ToByteArray()];
}
