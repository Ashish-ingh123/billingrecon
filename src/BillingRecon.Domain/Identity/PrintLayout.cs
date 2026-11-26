using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// How a hospital prints one kind of document (ADR 0059): the paper and its margins, where the logo stands and how
/// large, or pre-printed stationery with the space its own head takes, what is shown and what is left out, and a footer
/// line and terms. It changes the layout of a sheet and nothing else: no figure, no mark of the original or a duplicate.
/// </summary>
public sealed record PrintLayout
{
    /// <summary>The narrowest margin, in millimetres.</summary>
    public const int MarginMinMm = 5;

    /// <summary>The widest margin, in millimetres.</summary>
    public const int MarginMaxMm = 25;

    /// <summary>The most space pre-printed stationery may take at the top, in millimetres.</summary>
    public const int StationeryTopMaxMm = 80;

    /// <summary>Longest footer line.</summary>
    public const int FooterLineMaxLength = 200;

    /// <summary>Longest terms.</summary>
    public const int TermsMaxLength = 1000;

    /// <summary>The paper.</summary>
    public PaperSize Paper { get; init; } = PaperSize.A4;

    /// <summary>The top margin in millimetres.</summary>
    public int MarginTopMm { get; init; } = 14;

    /// <summary>The right margin in millimetres.</summary>
    public int MarginRightMm { get; init; } = 12;

    /// <summary>The bottom margin in millimetres.</summary>
    public int MarginBottomMm { get; init; } = 14;

    /// <summary>The left margin in millimetres.</summary>
    public int MarginLeftMm { get; init; } = 12;

    /// <summary>Where the logo stands.</summary>
    public LogoPosition LogoPosition { get; init; } = LogoPosition.Left;

    /// <summary>How large the logo is.</summary>
    public LogoSize LogoSize { get; init; } = LogoSize.Medium;

    /// <summary>Whether the paper carries the hospital's head already: the sheet then prints no letterhead of its own.</summary>
    public bool PrePrintedStationery { get; init; }

    /// <summary>The space left empty at the top for the head of pre-printed stationery, in millimetres.</summary>
    public int StationeryTopMm { get; init; }

    /// <summary>Whether the patient's phone and address are printed.</summary>
    public bool ShowPatientContact { get; init; }

    /// <summary>Whether the payer is printed.</summary>
    public bool ShowPayer { get; init; } = true;

    /// <summary>Whether a bill prints the SAC column. Always true for a bill (see <see cref="Problems"/>).</summary>
    public bool ShowSacColumn { get; init; } = true;

    /// <summary>Whether a bill prints its tax in its CGST and SGST halves. Always true for a bill.</summary>
    public bool ShowTaxSplit { get; init; } = true;

    /// <summary>Whether the amount is printed in words.</summary>
    public bool ShowAmountInWords { get; init; } = true;

    /// <summary>Whether the signature block is printed.</summary>
    public bool ShowSignatureBlock { get; init; } = true;

    /// <summary>Whether a receipt or a refund voucher names who recorded it.</summary>
    public bool ShowCashierName { get; init; }

    /// <summary>A line at the foot of the sheet; null for none.</summary>
    public string? FooterLine { get; init; }

    /// <summary>Terms printed above the foot, as plain text; null for none.</summary>
    public string? Terms { get; init; }

    /// <summary>
    /// The layout every kind of document has until its hospital sets one: the sheet as it was before there were
    /// settings (ADR 0054 as amended), on A4 with the margins of the print styles.
    /// </summary>
    public static PrintLayout Default { get; } = new();

    /// <summary>
    /// What is wrong with this layout for <paramref name="documentType"/>, by the name of its part; empty when nothing
    /// is. A bill that carries tax must show the SAC code of each service and the tax in its halves (ADR 0054, point 4;
    /// rule 46 of the CGST rules), so neither can be switched off for a bill; and a bill is not printed on a roll.
    /// </summary>
    public IReadOnlyDictionary<string, string> Problems(PrintedDocumentType documentType)
    {
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Enum.IsDefined(Paper))
        {
            problems[nameof(Paper)] = "The paper is A4, A5 or Roll80.";
        }
        else if (Paper == PaperSize.Roll80 && documentType == PrintedDocumentType.Bill)
        {
            problems[nameof(Paper)] = "A bill is printed on A4 or A5: its lines do not fit a roll.";
        }

        foreach (var (name, value) in new[] { (nameof(MarginTopMm), MarginTopMm), (nameof(MarginRightMm), MarginRightMm), (nameof(MarginBottomMm), MarginBottomMm), (nameof(MarginLeftMm), MarginLeftMm) })
        {
            if (value is < MarginMinMm or > MarginMaxMm)
            {
                problems[name] = $"A margin is between {MarginMinMm} and {MarginMaxMm} mm.";
            }
        }

        if (!Enum.IsDefined(LogoPosition))
        {
            problems[nameof(LogoPosition)] = "The logo stands at the Left, in the Centre or at the Right.";
        }

        if (!Enum.IsDefined(LogoSize))
        {
            problems[nameof(LogoSize)] = "The logo is Small, Medium or Large.";
        }

        if (StationeryTopMm is < 0 or > StationeryTopMaxMm)
        {
            problems[nameof(StationeryTopMm)] = $"The space for pre-printed stationery is between 0 and {StationeryTopMaxMm} mm.";
        }

        if (documentType == PrintedDocumentType.Bill)
        {
            if (!ShowSacColumn)
            {
                problems[nameof(ShowSacColumn)] = "A tax invoice must show the SAC code of each service: it cannot be switched off for a bill.";
            }

            if (!ShowTaxSplit)
            {
                problems[nameof(ShowTaxSplit)] = "A tax invoice must show its tax as CGST and SGST: it cannot be switched off for a bill.";
            }
        }

        if (FooterLine is { Length: > FooterLineMaxLength })
        {
            problems[nameof(FooterLine)] = $"The footer line has at most {FooterLineMaxLength} characters.";
        }

        if (Terms is { Length: > TermsMaxLength })
        {
            problems[nameof(Terms)] = $"The terms have at most {TermsMaxLength} characters.";
        }

        return problems;
    }
}
