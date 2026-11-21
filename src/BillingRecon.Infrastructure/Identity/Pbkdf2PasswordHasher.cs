using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BillingRecon.Application.Identity;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// PBKDF2 with HMAC-SHA-256, through the static <see cref="Rfc2898DeriveBytes.Pbkdf2(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, HashAlgorithmName, int)"/>
/// of the BCL (docs/database.md section 3.1; ADR 0030): 600,000 iterations, the figure OWASP gives for this function,
/// a random salt of 16 bytes, a hash of 32. The text kept is <c>pbkdf2-sha256$iterations$salt$hash</c>, salt and hash
/// in base64, so that a later change of the iterations still verifies the hashes made before it. Comparison takes
/// the same time whatever the input (<see cref="CryptographicOperations.FixedTimeEquals"/>).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>The name of the algorithm in the stored text.</summary>
    public const string Algorithm = "pbkdf2-sha256";

    /// <summary>Iterations of a new hash.</summary>
    public const int Iterations = 600_000;

    /// <summary>
    /// A hash no password matches: the development user has it until a password is set (ADR 0030). Not in the format,
    /// so <see cref="Verify"/> refuses it before any work.
    /// </summary>
    public const string Unusable = "!";

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const char Separator = '$';

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);

        return string.Join(Separator, Algorithm, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(hash);

        var parts = hash.Split(Separator);
        if (parts.Length != 4
            || parts[0] != Algorithm
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1
            || !TryFromBase64(parts[2], out var salt)
            || !TryFromBase64(parts[3], out var expected)
            || expected.Length != HashBytes)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations), expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    private static bool TryFromBase64(string text, out byte[] bytes)
    {
        var buffer = new byte[text.Length];
        if (Convert.TryFromBase64String(text, buffer, out var written))
        {
            bytes = buffer[..written];

            return true;
        }

        bytes = [];

        return false;
    }
}
