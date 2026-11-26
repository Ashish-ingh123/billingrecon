using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// How the hospital prints one kind of document (ADR 0059; docs/api.md section 8.19). A kind the hospital has saved no
/// settings for answers the defaults with <see cref="PrintSettingsMapping.NoVersion"/> as its version, which is what
/// its first save sends in <c>If-Match</c>.
/// </summary>
public sealed record PrintSettingsDto(
    PrintedDocumentType DocumentType,
    PaperSize Paper,
    int MarginTopMm,
    int MarginRightMm,
    int MarginBottomMm,
    int MarginLeftMm,
    LogoPosition LogoPosition,
    LogoSize LogoSize,
    bool PrePrintedStationery,
    int StationeryTopMm,
    bool ShowPatientContact,
    bool ShowPayer,
    bool ShowSacColumn,
    bool ShowTaxSplit,
    bool ShowAmountInWords,
    bool ShowSignatureBlock,
    bool ShowCashierName,
    string? FooterLine,
    string? Terms,
    string Version);
