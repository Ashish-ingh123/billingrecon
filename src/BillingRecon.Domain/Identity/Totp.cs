using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// The time-based one-time code of the second step of a sign-in (RFC 6238 over RFC 4226; ADR 0061): six digits from
/// HMAC-SHA-1 of the count of 30-second steps since 1970, which is what authenticator apps compute from the secret a
/// QR code gave them. Pure arithmetic on the secret and an instant: nothing is read from a clock here.
/// </summary>
public static class Totp
{
    /// <summary>The digits of a code.</summary>
    public const int Digits = 6;

    /// <summary>The length of a time step.</summary>
    public const int StepSeconds = 30;

    /// <summary>The bytes of a secret: 160 bits, the size of the hash, as RFC 4226 recommends.</summary>
    public const int SecretBytes = 20;

    /// <summary>How many steps before and after the current one a code is still accepted, for a clock that drifts.</summary>
    public const int Window = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The step an instant falls in: the whole 30-second steps since 1970-01-01T00:00:00Z.</summary>
    public static long StepOf(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The code of <paramref name="step"/> for <paramref name="secret"/>, with its leading zeros.</summary>
    public static string CodeOf(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        // HMAC-SHA-1 is what RFC 6238 specifies and every authenticator app implements; the weakness of SHA-1 against
        // collisions does not touch an HMAC, and no other algorithm is read by the common apps.
#pragma warning disable CA5350
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350

        // The dynamic truncation of RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7FFFFFFF;

        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether <paramref name="code"/> is the code of the step of <paramref name="now"/> or of a neighbouring one, and
    /// of a step after <paramref name="lastUsedStep"/>: a code is good once, so the step it was accepted for and every
    /// earlier one are spent. <paramref name="matchedStep"/> is the step to remember. Every candidate is compared in
    /// constant time.
    /// </summary>
    public static bool TryMatch(ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        Span<byte> given = stackalloc byte[Digits];
        Span<byte> expected = stackalloc byte[Digits];
        for (var index = 0; index < Digits; index++)
        {
            given[index] = (byte)code[index];
        }

        var current = StepOf(now);
        var found = false;
        for (var step = current - Window; step <= current + Window; step++)
        {
            var candidate = CodeOf(secret, step);
            for (var index = 0; index < Digits; index++)
            {
                expected[index] = (byte)candidate[index];
            }

            if (CryptographicOperations.FixedTimeEquals(given, expected) && (lastUsedStep is not { } last || step > last) && !found)
            {
                matchedStep = step;
                found = true;
            }
        }

        return found;
    }

    /// <summary>A secret as an authenticator app is given it: base32 without padding (RFC 4648).</summary>
    public static string ToBase32(ReadOnlySpan<byte> bytes)
    {
        var text = new char[((bytes.Length * 8) + 4) / 5];
        var buffer = 0;
        var bits = 0;
        var written = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                text[written++] = Base32Alphabet[(buffer >> bits) & 0x1F];
            }
        }

        if (bits > 0)
        {
            text[written++] = Base32Alphabet[(buffer << (5 - bits)) & 0x1F];
        }

        return new string(text, 0, written);
    }

    /// <summary>
    /// The address an authenticator app reads from the QR code (the Key Uri Format): the issuer, the account as the
    /// user knows it, and the secret in base32. The algorithm, the digits and the period are the defaults of every app
    /// and are named all the same.
    /// </summary>
    public static Uri ProvisioningUri(string issuer, string account, ReadOnlySpan<byte> secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}";

        return new Uri(
            $"otpauth://totp/{label}?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits.ToString(CultureInfo.InvariantCulture)}&period={StepSeconds.ToString(CultureInfo.InvariantCulture)}");
    }
}
