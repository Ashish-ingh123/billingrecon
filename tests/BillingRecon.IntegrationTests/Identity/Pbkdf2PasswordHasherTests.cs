using BillingRecon.Infrastructure.Identity;

namespace BillingRecon.IntegrationTests.Identity;

/// <summary>
/// The password hasher (BR-601; docs/database.md section 3.1; ADR 0030): PBKDF2-HMAC-SHA-256 with 600,000 iterations and
/// a salt of its own per hash, in the stored format <c>pbkdf2-sha256$iterations$salt$hash</c>; a password is checked
/// against any iteration count the text names, and a text it cannot read matches no password.
/// </summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void A_hash_has_the_documented_format_and_never_contains_the_password()
    {
        var hash = _hasher.Hash(Password);

        var parts = hash.Split('$');
        parts.Length.ShouldBe(4);
        (parts[0], parts[1]).ShouldBe(("pbkdf2-sha256", "600000"));
        Convert.FromBase64String(parts[2]).Length.ShouldBe(16, "a salt of 16 bytes");
        Convert.FromBase64String(parts[3]).Length.ShouldBe(32, "a hash of 32 bytes");
        hash.ShouldNotContain(Password);
        hash.Length.ShouldBeLessThanOrEqualTo(500, "it fits Users.PasswordHash");
    }

    [Fact]
    public void The_right_password_matches_and_a_wrong_one_does_not()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(Password, hash).ShouldBeTrue();
        _hasher.Verify(Password + " ", hash).ShouldBeFalse();
        _hasher.Verify("Correct horse battery staple", hash).ShouldBeFalse("passwords are compared exactly");
    }

    [Fact]
    public void Every_hash_has_a_salt_of_its_own()
        => _hasher.Hash(Password).ShouldNotBe(_hasher.Hash(Password));

    [Fact]
    public void A_hash_made_with_another_iteration_count_still_verifies()
    {
        // The stored text names its iterations, so raising them later leaves the hashes made before valid.
        const string madeWithOneThousand = "pbkdf2-sha256$1000$AAECAwQFBgcICQoLDA0ODw==$";
        var expected = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(Password), Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODw=="), 1000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

        _hasher.Verify(Password, madeWithOneThousand + Convert.ToBase64String(expected)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Pbkdf2PasswordHasher.Unusable)]
    [InlineData("")]
    [InlineData("pbkdf2-sha256$600000$c2FsdA==")]
    [InlineData("pbkdf2-sha512$600000$AAECAwQFBgcICQoLDA0ODw==$AAECAwQFBgcICQoLDA0ODwABAgMEBQYHCAkKCwwNDg8=")]
    [InlineData("pbkdf2-sha256$0$AAECAwQFBgcICQoLDA0ODw==$AAECAwQFBgcICQoLDA0ODwABAgMEBQYHCAkKCwwNDg8=")]
    [InlineData("pbkdf2-sha256$600000$not base64$AAECAwQFBgcICQoLDA0ODwABAgMEBQYHCAkKCwwNDg8=")]
    [InlineData("pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0ODw==$c2hvcnQ=")]
    public void A_text_that_is_not_a_hash_of_this_format_matches_no_password(string hash)
        => _hasher.Verify(Password, hash).ShouldBeFalse();
}
