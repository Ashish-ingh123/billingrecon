using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The print settings as the API answers them (ADR 0059).</summary>
public static class PrintSettingsMapping
{
    /// <summary>The version of settings that were never saved: eight zero bytes, as no stored row ever has.</summary>
    public static string NoVersion { get; } = Convert.ToBase64String(new byte[8]);

    /// <summary>The kinds of document that are printed, in the order they are answered.</summary>
    public static IReadOnlyList<PrintedDocumentType> DocumentTypes { get; } = [PrintedDocumentType.Bill, PrintedDocumentType.Payment, PrintedDocumentType.Refund];

    /// <summary><paramref name="settings"/>, with their version.</summary>
    public static PrintSettingsDto ToDto(this PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return ToDto(settings.DocumentType, settings.Layout, RowVersionText.Of(settings));
    }

    /// <summary>The defaults of <paramref name="documentType"/>, for a hospital that has saved none.</summary>
    public static PrintSettingsDto DefaultDto(PrintedDocumentType documentType) => ToDto(documentType, PrintLayout.Default, NoVersion);

    private static PrintSettingsDto ToDto(PrintedDocumentType documentType, PrintLayout layout, string version) => new(
        DocumentType: documentType,
        Paper: layout.Paper,
        MarginTopMm: layout.MarginTopMm,
        MarginRightMm: layout.MarginRightMm,
        MarginBottomMm: layout.MarginBottomMm,
        MarginLeftMm: layout.MarginLeftMm,
        LogoPosition: layout.LogoPosition,
        LogoSize: layout.LogoSize,
        PrePrintedStationery: layout.PrePrintedStationery,
        StationeryTopMm: layout.StationeryTopMm,
        ShowPatientContact: layout.ShowPatientContact,
        ShowPayer: layout.ShowPayer,
        ShowSacColumn: layout.ShowSacColumn,
        ShowTaxSplit: layout.ShowTaxSplit,
        ShowAmountInWords: layout.ShowAmountInWords,
        ShowSignatureBlock: layout.ShowSignatureBlock,
        ShowCashierName: layout.ShowCashierName,
        FooterLine: layout.FooterLine,
        Terms: layout.Terms,
        Version: version);
}
