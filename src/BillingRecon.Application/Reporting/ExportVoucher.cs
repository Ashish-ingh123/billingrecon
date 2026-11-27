
namespace BillingRecon.Application.Reporting;

/// <summary>
/// One voucher of the export: one posting of our ledger (ADR 0062). It balances because the posting does, and its
/// number is that of our document.
/// </summary>
/// <param name="PostingId">The posting, which names the voucher for the package so that it is told apart from every other.</param>
/// <param name="BusinessDate">The business date of the posting.</param>
/// <param name="Type">The type of voucher.</param>
/// <param name="Number">Our document number; for a posting that is not the document's own (an allocation, a reversal) with a suffix that makes it one of a kind.</param>
/// <param name="Narration">What happened, with the numbers of the documents and the payer's name.</param>
/// <param name="Lines">The lines, debits and credits equal.</param>
public sealed record ExportVoucher(Guid PostingId, DateOnly BusinessDate, ExportVoucherType Type, string Number, string Narration, IReadOnlyList<ExportVoucherLine> Lines);
