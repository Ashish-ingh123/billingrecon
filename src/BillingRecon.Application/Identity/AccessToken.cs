using System.Globalization;
using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>A token as <see cref="ITokenIssuer"/> issues it: the compact text, and when it stops being accepted.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    // The token is a bearer secret: a text made of it, in a log line or an exception, leaves the value out.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresAtUtc = ").Append(ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));

        return true;
    }
}
