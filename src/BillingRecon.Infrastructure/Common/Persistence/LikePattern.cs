namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The pattern of a search that matches a part of a text (Prompt 10.2b): <c>LIKE '%text%'</c>, in which the search is
/// data and never a pattern. Each character LIKE gives a meaning to, and the escape character itself, stands behind
/// the escape character, so a <c>%</c>, <c>_</c> or <c>[</c> of the search matches itself. A read service passes
/// <see cref="Escape"/> as the third argument of <c>EF.Functions.Like</c>.
/// </summary>
internal static class LikePattern
{
    /// <summary>The escape character of every such pattern.</summary>
    public const string Escape = "\\";

    /// <summary>The pattern that finds <paramref name="search"/> anywhere in a text.</summary>
    public static string Containing(string search)
    {
        ArgumentNullException.ThrowIfNull(search);

        var text = search
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal)
            .Replace("[", Escape + "[", StringComparison.Ordinal);

        return $"%{text}%";
    }
}
