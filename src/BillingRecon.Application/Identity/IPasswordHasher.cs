namespace BillingRecon.Application.Identity;

/// <summary>
/// Turns a password into the text a <c>User</c> keeps, and checks a password against it (BR-601; ADR 0030). The
/// implementation is slow and salted on purpose; the password itself is never stored, logged or audited.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>A new hash of <paramref name="password"/>, with a salt of its own.</summary>
    public string Hash(string password);

    /// <summary>
    /// Whether <paramref name="password"/> is the one <paramref name="hash"/> was made from. A hash this implementation
    /// cannot read, such as the unusable one of the development user, matches no password.
    /// </summary>
    public bool Verify(string password, string hash);

    /// <summary>
    /// Does the work of one verification of <paramref name="password"/>, against a hash no password was made from, and
    /// answers false. A refused sign-in for an address nobody has then takes as long as one with a wrong password, so the
    /// time of the answer does not tell which addresses exist (ADR 0031).
    /// </summary>
    public bool VerifyWithoutHash(string password);
}
