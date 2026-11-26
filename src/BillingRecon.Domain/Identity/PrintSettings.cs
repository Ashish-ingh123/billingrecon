using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// How a hospital prints one kind of document (ADR 0059): one row for its bills, one for its receipts, one for its
/// refund vouchers, each made the first time its administrator saves it. A kind with no row prints by
/// <see cref="PrintLayout.Default"/>. Every change is an event, and so an audit record with what it was and is.
/// </summary>
public sealed class PrintSettings : AggregateRoot<PrintSettingsId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    // Entity Framework creates the settings with this constructor and then sets the properties.
    private PrintSettings()
    {
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The kind of document these settings print.</summary>
    public PrintedDocumentType DocumentType { get; private set; }

    /// <summary>The paper.</summary>
    public PaperSize Paper { get; private set; }

    /// <summary>The top margin in millimetres.</summary>
    public int MarginTopMm { get; private set; }

    /// <summary>The right margin in millimetres.</summary>
    public int MarginRightMm { get; private set; }

    /// <summary>The bottom margin in millimetres.</summary>
    public int MarginBottomMm { get; private set; }

    /// <summary>The left margin in millimetres.</summary>
    public int MarginLeftMm { get; private set; }

    /// <summary>Where the logo stands.</summary>
    public LogoPosition LogoPosition { get; private set; }

    /// <summary>How large the logo is.</summary>
    public LogoSize LogoSize { get; private set; }

    /// <summary>Whether the paper carries the hospital's head already.</summary>
    public bool PrePrintedStationery { get; private set; }

    /// <summary>The space left at the top for pre-printed stationery, in millimetres.</summary>
    public int StationeryTopMm { get; private set; }

    /// <summary>Whether the patient's phone and address are printed.</summary>
    public bool ShowPatientContact { get; private set; }

    /// <summary>Whether the payer is printed.</summary>
    public bool ShowPayer { get; private set; }

    /// <summary>Whether the SAC column is printed.</summary>
    public bool ShowSacColumn { get; private set; }

    /// <summary>Whether the tax is printed in its halves.</summary>
    public bool ShowTaxSplit { get; private set; }

    /// <summary>Whether the amount is printed in words.</summary>
    public bool ShowAmountInWords { get; private set; }

    /// <summary>Whether the signature block is printed.</summary>
    public bool ShowSignatureBlock { get; private set; }

    /// <summary>Whether who recorded the document is named.</summary>
    public bool ShowCashierName { get; private set; }

    /// <summary>A line at the foot of the sheet.</summary>
    public string? FooterLine { get; private set; }

    /// <summary>Terms printed above the foot.</summary>
    public string? Terms { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>The settings as one value.</summary>
    public PrintLayout Layout => new()
    {
        Paper = Paper,
        MarginTopMm = MarginTopMm,
        MarginRightMm = MarginRightMm,
        MarginBottomMm = MarginBottomMm,
        MarginLeftMm = MarginLeftMm,
        LogoPosition = LogoPosition,
        LogoSize = LogoSize,
        PrePrintedStationery = PrePrintedStationery,
        StationeryTopMm = StationeryTopMm,
        ShowPatientContact = ShowPatientContact,
        ShowPayer = ShowPayer,
        ShowSacColumn = ShowSacColumn,
        ShowTaxSplit = ShowTaxSplit,
        ShowAmountInWords = ShowAmountInWords,
        ShowSignatureBlock = ShowSignatureBlock,
        ShowCashierName = ShowCashierName,
        FooterLine = FooterLine,
        Terms = Terms,
    };

    /// <summary>The first settings a hospital saves for <paramref name="documentType"/>.</summary>
    /// <exception cref="ArgumentException">The kind is not defined, or the layout is not one this kind may have: bugs in the caller, whose validator refuses them first.</exception>
    public static PrintSettings Create(PrintedDocumentType documentType, PrintLayout layout)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new ArgumentException("The kind of document is not one the enum defines.", nameof(documentType));
        }

        var settings = new PrintSettings { DocumentType = documentType };
        settings.Take(Checked(documentType, layout));

        // The id is still empty here: it is assigned when the settings are added to the unit of work.
        settings.AddDomainEvent(new PrintSettingsChanged(documentType));

        return settings;
    }

    /// <summary>
    /// A TenantAdmin changes the settings. Texts are trimmed, and a blank one is none. Returns false, and records
    /// nothing, when nothing changes.
    /// </summary>
    /// <exception cref="ArgumentException">The layout is not one this kind may have: a bug in the caller, whose validator refuses it first.</exception>
    public bool Change(PrintLayout layout)
    {
        var next = Checked(DocumentType, layout);
        if (next == Layout)
        {
            return false;
        }

        Take(next);
        AddDomainEvent(new PrintSettingsChanged(DocumentType));

        return true;
    }

    private static PrintLayout Checked(PrintedDocumentType documentType, PrintLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var next = layout with
        {
            FooterLine = Field.Optional(layout.FooterLine, PrintLayout.FooterLineMaxLength),
            Terms = Field.Optional(layout.Terms, PrintLayout.TermsMaxLength),
        };

        var problems = next.Problems(documentType);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems.Values), nameof(layout));
        }

        return next;
    }

    private void Take(PrintLayout layout)
    {
        Paper = layout.Paper;
        MarginTopMm = layout.MarginTopMm;
        MarginRightMm = layout.MarginRightMm;
        MarginBottomMm = layout.MarginBottomMm;
        MarginLeftMm = layout.MarginLeftMm;
        LogoPosition = layout.LogoPosition;
        LogoSize = layout.LogoSize;
        PrePrintedStationery = layout.PrePrintedStationery;
        StationeryTopMm = layout.StationeryTopMm;
        ShowPatientContact = layout.ShowPatientContact;
        ShowPayer = layout.ShowPayer;
        ShowSacColumn = layout.ShowSacColumn;
        ShowTaxSplit = layout.ShowTaxSplit;
        ShowAmountInWords = layout.ShowAmountInWords;
        ShowSignatureBlock = layout.ShowSignatureBlock;
        ShowCashierName = layout.ShowCashierName;
        FooterLine = layout.FooterLine;
        Terms = layout.Terms;
    }
}
