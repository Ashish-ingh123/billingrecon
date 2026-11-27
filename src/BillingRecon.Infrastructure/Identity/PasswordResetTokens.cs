using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The tokens of the links: the 16 bytes of the user's id and 32 random bytes, in base64 for an address; stored and
/// found by the SHA-256 of the whole text, in hexadecimal.
/// </summary>
public sealed class PasswordResetTokens : IPasswordResetTokens
{
    private const int IdBytes = 16;
    private const int RandomBytes = 32;

    /// <inheritdoc />
    public (string Token, string Hash) New(UserId userId)
    {
        Span<byte> bytes = stackalloc byte[IdBytes + RandomBytes];
        userId.Value.TryWriteBytes(bytes, bigEndian: true, out _);
        RandomNumberGenerator.Fill(bytes[IdBytes..]);
        var token = Base64Url.EncodeToString(bytes);

        return (token, HashOf(token));
    }

    /// <inheritdoc />
    public bool TryRead(string? token, out UserId userId, out string hash)
    {
        userId = default;
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[IdBytes + RandomBytes];
        var text = token.Trim();
        if (text.Length > 100 || !Base64Url.IsValid(text) || !Base64Url.TryDecodeFromChars(text, bytes, out var written) || written != bytes.Length)
        {
            return false;
        }

        userId = new UserId(new Guid(bytes[..IdBytes], bigEndian: true));
        hash = HashOf(text);

        return userId.Value != Guid.Empty;
    }

    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
