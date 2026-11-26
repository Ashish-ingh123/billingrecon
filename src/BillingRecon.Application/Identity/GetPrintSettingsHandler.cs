using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Reads how the hospital prints each kind of document (ADR 0059), for anyone who is signed in, as the letterhead is
/// read: whoever prints a document needs its layout. Always three answers, in the order bill, receipt, refund voucher;
/// a kind with no saved settings answers the defaults.
/// </summary>
public sealed class GetPrintSettingsHandler(IPrintSettingsRepository settings) : IQueryHandler<GetPrintSettingsQuery, IReadOnlyList<PrintSettingsDto>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PrintSettingsDto>>> HandleAsync(GetPrintSettingsQuery query, CancellationToken ct)
    {
        var saved = await settings.ListAsync(ct);

        return PrintSettingsMapping.DocumentTypes
            .Select(documentType => saved.SingleOrDefault(row => row.DocumentType == documentType)?.ToDto() ?? PrintSettingsMapping.DefaultDto(documentType))
            .ToList();
    }
}
