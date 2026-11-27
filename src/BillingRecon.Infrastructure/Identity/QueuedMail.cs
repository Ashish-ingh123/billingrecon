using System.Text;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>A mail on its way: to whom, about what, saying what. Plain text only.</summary>
/// <param name="To">The address it goes to.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Body">Its text.</param>
public sealed record QueuedMail(string To, string Subject, string Body)
{
    // The body may hold a link that signs nobody in but resets a password: a text made of the mail leaves it out.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Subject = ").Append(Subject).Append(", Body = ").Append(Body.Length > 0 ? "***" : string.Empty);

        return true;
    }
}
