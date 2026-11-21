using System.Diagnostics.CodeAnalysis;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// The key that signs the API's tokens and checks them (ADR 0031, point 2): <c>Jwt:SigningKey</c>, in base64, at least
/// 256 bits, checked when the API starts. The other terms of a token are fixed here, not configured: who issues it, for
/// whom, how long it lives and how far two clocks may disagree.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Jwt";

    /// <summary>The <c>iss</c> of every token, checked on every request.</summary>
    public const string Issuer = "BillingRecon";

    /// <summary>The <c>aud</c> of every token, checked on every request.</summary>
    public const string Audience = "BillingRecon.Api";

    /// <summary>The shortest key HS256 is given: 256 bits.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The key of <c>appsettings.Development.json</c>. It is in the repository, so outside Development the API refuses
    /// to start with it.
    /// </summary>
    public const string DevelopmentSigningKey = "PMgeGvqlvJ08AGPC97+03Anqp1ZJyJrp8LTsYMQN9z4=";

    /// <summary>Why the API does not start when the key is missing, is not base64 or is too short.</summary>
    public const string InvalidKeyMessage =
        "Jwt:SigningKey is required: a key of at least 256 bits (32 random bytes) in base64, kept as a secret (docs/runbook.md).";

    /// <summary>Why the API does not start outside Development with the key of the repository.</summary>
    public const string DevelopmentKeyMessage =
        "Jwt:SigningKey is the development key of appsettings.Development.json, which anyone can read; outside the Development environment set a key of your own.";

    /// <summary>How long a token is valid (ADR 0031, point 2). There is no refresh token: the client signs in again.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    /// <summary>How far the clock of the API and the clock a token was issued by may disagree.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>The signing key, in base64.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>True when <see cref="SigningKey"/> is base64 of at least <see cref="MinimumKeyBytes"/> bytes.</summary>
    public static bool HasValidKey(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return TryReadKey(options.SigningKey, out _);
    }

    /// <summary>True unless <see cref="SigningKey"/> is the key of the repository (<see cref="DevelopmentSigningKey"/>).</summary>
    public static bool IsNotTheDevelopmentKey(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !TryReadKey(options.SigningKey, out var key)
            || !TryReadKey(DevelopmentSigningKey, out var developmentKey)
            || !key.AsSpan().SequenceEqual(developmentKey);
    }

    /// <summary>The bytes of the key. Throws when the key is not valid, which the check at startup rules out.</summary>
    public byte[] ReadKey() => TryReadKey(SigningKey, out var key) ? key : throw new InvalidOperationException(InvalidKeyMessage);

    private static bool TryReadKey(string? text, [NotNullWhen(true)] out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var buffer = new byte[text.Length];
        if (!Convert.TryFromBase64String(text.Trim(), buffer, out var written) || written < MinimumKeyBytes)
        {
            return false;
        }

        key = buffer[..written];

        return true;
    }
}
