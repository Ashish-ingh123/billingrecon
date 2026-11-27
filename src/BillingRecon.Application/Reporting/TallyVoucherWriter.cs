using System.Globalization;
using System.Text;
using System.Xml;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// Writes an export as the XML file Tally imports as vouchers (ADR 0062). The shape follows Tally's own
/// documentation: the envelope of an "Import Data" request for the report "Vouchers", a <c>TALLYMESSAGE</c> for each
/// voucher, and in a voucher its date as <c>YYYYMMDD</c>, type, number, narration and one
/// <c>ALLLEDGERENTRIES.LIST</c> for each line, where a debit is <c>ISDEEMEDPOSITIVE</c> Yes with a negative amount and
/// a credit No with a positive one. That sign rule is what every sample of Tally's shows and no sentence of it says,
/// and no file of ours has been imported into a Tally: <c>docs/go-live.md</c> has the accountant do so first.
/// </summary>
public static class TallyVoucherWriter
{
    /// <summary>The media type of the file.</summary>
    public const string ContentType = "application/xml";

    /// <summary>The name of the file of a period.</summary>
    public static string FileName(DateOnly from, DateOnly to)
        => string.Create(CultureInfo.InvariantCulture, $"billingrecon-tally-{from:yyyyMMdd}-{to:yyyyMMdd}.xml");

    /// <summary>Writes <paramref name="export"/> to <paramref name="output"/> as UTF-8, a tag to a line.</summary>
    public static async Task WriteAsync(Stream output, AccountingExport export, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(export);

        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = " ",
            NewLineChars = "\r\n",
            CloseOutput = false,
        };

        await using var xml = XmlWriter.Create(output, settings);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "ENVELOPE", null);
        await xml.WriteStartElementAsync(null, "HEADER", null);
        await xml.WriteElementStringAsync(null, "TALLYREQUEST", null, "Import Data");
        await xml.WriteEndElementAsync();
        await xml.WriteStartElementAsync(null, "BODY", null);
        await xml.WriteStartElementAsync(null, "IMPORTDATA", null);
        await xml.WriteStartElementAsync(null, "REQUESTDESC", null);
        await xml.WriteElementStringAsync(null, "REPORTNAME", null, "Vouchers");
        await xml.WriteEndElementAsync();
        await xml.WriteStartElementAsync(null, "REQUESTDATA", null);

        foreach (var voucher in export.Vouchers)
        {
            ct.ThrowIfCancellationRequested();

            var type = voucher.Type.ToString();
            await xml.WriteStartElementAsync(null, "TALLYMESSAGE", null);
            await xml.WriteStartElementAsync(null, "VOUCHER", null);
            await xml.WriteAttributeStringAsync(null, "REMOTEID", null, $"billingrecon-{voucher.PostingId:N}");
            await xml.WriteAttributeStringAsync(null, "VCHTYPE", null, type);
            await xml.WriteAttributeStringAsync(null, "ACTION", null, "Create");
            await xml.WriteElementStringAsync(null, "DATE", null, voucher.BusinessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            await xml.WriteElementStringAsync(null, "VOUCHERTYPENAME", null, type);
            await xml.WriteElementStringAsync(null, "VOUCHERNUMBER", null, voucher.Number);
            await xml.WriteElementStringAsync(null, "NARRATION", null, Clean(voucher.Narration));

            foreach (var line in voucher.Lines)
            {
                var debit = line.Debit != 0;
                await xml.WriteStartElementAsync(null, "ALLLEDGERENTRIES.LIST", null);
                await xml.WriteElementStringAsync(null, "LEDGERNAME", null, export.Names[line.Ledger]);
                await xml.WriteElementStringAsync(null, "ISDEEMEDPOSITIVE", null, debit ? "Yes" : "No");
                await xml.WriteElementStringAsync(null, "AMOUNT", null, (debit ? -line.Debit : line.Credit).ToString("0.00", CultureInfo.InvariantCulture));
                await xml.WriteEndElementAsync();
            }

            await xml.WriteEndElementAsync();
            await xml.WriteEndElementAsync();
        }

        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
        await xml.FlushAsync();
    }

    // A name may hold a character XML 1.0 cannot carry at all (a control character typed into a payer's name):
    // it becomes a space, so that the file stays one a parser reads.
    private static string Clean(string text)
        => string.Concat(text.Select(character => XmlConvert.IsXmlChar(character) && !char.IsControl(character) ? character : ' '));
}
