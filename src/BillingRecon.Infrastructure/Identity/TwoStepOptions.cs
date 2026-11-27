using System.Diagnostics.CodeAnalysis;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The second step of a sign-in (ADR 0061): <c>TwoStep:EncryptionKey</c>, the key that keeps the users' secrets
/// unreadable at rest, a secret of the host like the signing key of the tokens and never the same key; and
/// <c>TwoStep:Required</c>, which may switch the requirement off in the Development and Demo environments only.
/// </summary>
public sealed class TwoStepOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "TwoStep";

    /// <summary>The bytes of the key: AES-256.</summary>
    public const int KeyBytes = 32;

    /// <summary>The key of <c>appsettings.Development.json</c>, which anyone can read: refused outside Development.</summary>
    public const string DevelopmentEncryptionKey = "c2Vjb25kLXN0ZXAtZGV2ZWxvcG1lbnQta2V5LTAwMDE=";

    /// <summary>The message of a missing or malformed key.</summary>
    public const string InvalidKeyMessage =
        "TwoStep:EncryptionKey is required: a key of exactly 256 bits (32 random bytes) in base64, kept as a secret (docs/runbook.md).";

    /// <summary>The message of the development key used elsewhere.</summary>
    public const string DevelopmentKeyMessage =
        "TwoStep:EncryptionKey is the development key of appsettings.Development.json, which anyone can read; outside the Development environment set a key of your own.";

    /// <summary>The message of the requirement switched off where it may not be.</summary>
    public const string RequiredMessage =
        "TwoStep:Required may be false in the Development and Demo environments only: an administrator and a supervisor of a hospital sign in with a second step (ADR 0061).";

    /// <summary>The key, in base64.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>Whether a TenantAdmin and a Supervisor must have a second step. True unless a setting says otherwise.</summary>
    public bool Required { get; set; } = true;

    /// <summary>True when the key is 32 bytes of base64.</summary>
    public static bool HasValidKey(TwoStepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return TryReadKey(options.EncryptionKey, out _);
    }

    /// <summary>True unless the key is the development key.</summary>
    public static bool IsNotTheDevelopmentKey(TwoStepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !string.Equals(options.EncryptionKey.Trim(), DevelopmentEncryptionKey, StringComparison.Ordinal);
    }

    /// <summary>The key's bytes.</summary>
    /// <exception cref="InvalidOperationException">The key is missing or malformed: the startup validation should have stopped the API.</exception>
    public byte[] ReadKey() => TryReadKey(EncryptionKey, out var key) ? key : throw new InvalidOperationException(InvalidKeyMessage);

    private static bool TryReadKey(string? text, [NotNullWhen(true)] out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var buffer = new byte[text.Length];
        if (!Convert.TryFromBase64String(text.Trim(), buffer, out var written) || written != KeyBytes)
        {
            return false;
        }

        key = buffer[..written];

        return true;
    }
}
