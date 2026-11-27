using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// The cash of a drawer counted by denomination (BR-313; ADR 0060): how many pieces of each note and coin of
/// <see cref="Denominations"/>. It is held as one canonical text, every denomination named from the largest to the
/// smallest (<c>2000x0 500x12 ... 1x4</c>), so that two equal counts are one value and the count is one column of its
/// shift. What is below one rupee is not here: the shift holds it as an amount of its own.
/// </summary>
public readonly record struct CashCount
{
    /// <summary>The notes and coins that are counted, in rupees, the largest first.</summary>
    public static IReadOnlyList<int> Denominations { get; } = [2000, 500, 200, 100, 50, 20, 10, 5, 2, 1];

    /// <summary>The most pieces of one denomination a count holds.</summary>
    public const int MaxPieces = 99_999;

    /// <summary>Longest text a count can have: the width of its column.</summary>
    public const int TextMaxLength = 120;

    private CashCount(string text) => Text = text;

    /// <summary>The canonical text.</summary>
    public string Text { get; }

    /// <summary>
    /// A count from the pieces of each denomination. A denomination left out has none; false when one is not a
    /// denomination, is named twice, or has pieces below zero or above <see cref="MaxPieces"/>.
    /// </summary>
    public static bool TryOf(IEnumerable<(int Denomination, int Pieces)> pieces, out CashCount count)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        count = default;
        var given = new Dictionary<int, int>();
        foreach (var (denomination, number) in pieces)
        {
            if (!Denominations.Contains(denomination) || number < 0 || number > MaxPieces || !given.TryAdd(denomination, number))
            {
                return false;
            }
        }

        count = new CashCount(string.Join(
            ' ',
            Denominations.Select(denomination => string.Create(CultureInfo.InvariantCulture, $"{denomination}x{given.GetValueOrDefault(denomination)}"))));

        return true;
    }

    /// <summary>A count from the pieces of each denomination.</summary>
    /// <exception cref="ArgumentException">The pieces are not a count: a bug in the caller, whose validator refuses them first.</exception>
    public static CashCount Of(IEnumerable<(int Denomination, int Pieces)> pieces)
        => TryOf(pieces, out var count)
            ? count
            : throw new ArgumentException("A count names each denomination at most once, with 0 to 99,999 pieces.", nameof(pieces));

    /// <summary>A count from its stored text.</summary>
    /// <exception cref="FormatException">The text is not the canonical text of a count.</exception>
    public static CashCount Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parsed = new List<(int Denomination, int Pieces)>();
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var sides = part.Split('x');
            if (sides.Length != 2
                || !int.TryParse(sides[0], NumberStyles.None, CultureInfo.InvariantCulture, out var denomination)
                || !int.TryParse(sides[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pieces))
            {
                throw new FormatException("The text is not a count of cash by denomination.");
            }

            parsed.Add((denomination, pieces));
        }

        return TryOf(parsed, out var count) && count.Text == text
            ? count
            : throw new FormatException("The text is not a count of cash by denomination.");
    }

    /// <summary>The pieces of every denomination, the largest first, those with none included.</summary>
    public IReadOnlyList<(int Denomination, int Pieces)> Pieces()
        => Text is null
            ? [.. Denominations.Select(denomination => (denomination, 0))]
            : [.. Text.Split(' ').Select(part => part.Split('x')).Select(sides => (int.Parse(sides[0], CultureInfo.InvariantCulture), int.Parse(sides[1], CultureInfo.InvariantCulture)))];

    /// <summary>What the count comes to: the sum of each denomination times its pieces. Whole rupees, so exact.</summary>
    public Money Total() => Money.Of(Pieces().Sum(piece => (decimal)piece.Denomination * piece.Pieces));

    /// <inheritdoc />
    public override string ToString() => Text ?? string.Empty;
}
