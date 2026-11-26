using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// How one kind of document is printed (ADR 0059): every part is sent. Unknown members are refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePrintSettingsRequest
{
    /// <summary>The paper: A4, A5, or Roll80 for a receipt or a refund voucher.</summary>
    public required PaperSize Paper { get; init; }

    /// <summary>The top margin in millimetres, 5 to 25.</summary>
    public required int MarginTopMm { get; init; }

    /// <summary>The right margin in millimetres, 5 to 25.</summary>
    public required int MarginRightMm { get; init; }

    /// <summary>The bottom margin in millimetres, 5 to 25.</summary>
    public required int MarginBottomMm { get; init; }

    /// <summary>The left margin in millimetres, 5 to 25.</summary>
    public required int MarginLeftMm { get; init; }

    /// <summary>Where the logo stands: Left, Centre or Right.</summary>
    public required LogoPosition LogoPosition { get; init; }

    /// <summary>How large the logo is: Small, Medium or Large.</summary>
    public required LogoSize LogoSize { get; init; }

    /// <summary>Whether the paper carries the hospital's head already.</summary>
    public required bool PrePrintedStationery { get; init; }

    /// <summary>The space left at the top for pre-printed stationery, in millimetres, 0 to 80.</summary>
    public required int StationeryTopMm { get; init; }

    /// <summary>Whether the patient's phone and address are printed.</summary>
    public required bool ShowPatientContact { get; init; }

    /// <summary>Whether the payer is printed.</summary>
    public required bool ShowPayer { get; init; }

    /// <summary>Whether the SAC column is printed; always true for a bill.</summary>
    public required bool ShowSacColumn { get; init; }

    /// <summary>Whether the tax is printed in its halves; always true for a bill.</summary>
    public required bool ShowTaxSplit { get; init; }

    /// <summary>Whether the amount is printed in words.</summary>
    public required bool ShowAmountInWords { get; init; }

    /// <summary>Whether the signature block is printed.</summary>
    public required bool ShowSignatureBlock { get; init; }

    /// <summary>Whether who recorded a receipt or a refund voucher is named.</summary>
    public required bool ShowCashierName { get; init; }

    /// <summary>A line at the foot of the sheet, at most 200 characters.</summary>
    public string? FooterLine { get; init; }

    /// <summary>Terms printed above the foot, plain text, at most 1000 characters.</summary>
    public string? Terms { get; init; }

    /// <summary>The command, for the kind of document of the path and the version the client read.</summary>
    public UpdatePrintSettingsCommand ToCommand(PrintedDocumentType documentType, string expectedVersion) => new()
    {
        DocumentType = documentType,
        Layout = new PrintLayout
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
        },
        ExpectedVersion = expectedVersion,
    };
}
