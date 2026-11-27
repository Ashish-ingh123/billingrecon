using System.Security.Cryptography;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// The postings of a period as vouchers for the accountant (ADR 0062). **One posting is one voucher, and its lines are
/// the posting's entries**, so a voucher balances because the posting does and the vouchers of a period add up to the
/// ledger's entries of the period. Two things are said in the accountant's words instead of ours: an entry to
/// <c>Cash</c> goes to the ledger of the method the money came by (the payment's, or the refund's), and the credit to
/// <c>Revenue</c> of a bill, like the debit of a credit note, is split into its taxable value and its tax, read from
/// the document's own totals; the two parts add up to the entry. Everything is read through the context, so the tenant
/// filter applies, and nothing is tracked or written.
/// </summary>
public sealed class AccountingExportReports(BillingReconDbContext context) : IAccountingExportReports
{
    /// <inheritdoc />
    public async Task<ExportVouchers> VouchersAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        // "from" is a word of a query expression, so the period is named again for the three below.
        var (first, last) = (from, to);

        var postings = await context.Set<LedgerPosting>().AsNoTracking()
            .Where(posting => posting.BusinessDate >= first && posting.BusinessDate <= last)
            .OrderBy(posting => posting.BusinessDate)
            .ThenBy(posting => posting.PostedAtUtc)
            .ThenBy(posting => posting.Id)
            .Select(posting => new { posting.Id, posting.BusinessDate, posting.Kind, posting.DocumentType, posting.DocumentId, posting.PayerId, posting.Description })
            .ToListAsync(ct);

        var entries = await (
                from posting in context.Set<LedgerPosting>().AsNoTracking()
                join entry in context.Set<LedgerEntry>().AsNoTracking() on posting.Id equals entry.PostingId
                where posting.BusinessDate >= first && posting.BusinessDate <= last
                orderby entry.PostingId, entry.LineNo
                select new { entry.PostingId, entry.Account, entry.SubjectId, entry.Debit, entry.Credit })
            .ToListAsync(ct);

        // What the ledger itself says moved: its entries read again on their own and added up here (a sum of money is
        // not one the database is asked for, as in every report), not taken from the lines built below.
        var moved = await (
                from posting in context.Set<LedgerPosting>().AsNoTracking()
                join entry in context.Set<LedgerEntry>().AsNoTracking() on posting.Id equals entry.PostingId
                where posting.BusinessDate >= first && posting.BusinessDate <= last
                select new { entry.Debit, entry.Credit })
            .ToListAsync(ct);

        var linesOf = entries.ToLookup(entry => entry.PostingId);

        // An allocation's posting names the allocation; the payment or the credit note it belongs to is the subject of
        // its entry to UnappliedCash or CreditAvailable.
        Guid SubjectOf(LedgerPostingId posting, LedgerAccount account)
            => linesOf[posting].First(entry => entry.Account == account).SubjectId
                ?? throw new InvalidOperationException($"Posting {posting} has an entry to {account} without a subject.");

        var billIds = postings
            .Where(posting => posting.DocumentType == LedgerDocumentType.Bill)
            .Select(posting => new BillId(posting.DocumentId))
            .Distinct()
            .ToList();
        var paymentIds = postings
            .Where(posting => posting.DocumentType is LedgerDocumentType.Payment or LedgerDocumentType.PaymentAllocation)
            .Select(posting => new PaymentId(posting.DocumentType == LedgerDocumentType.Payment ? posting.DocumentId : SubjectOf(posting.Id, LedgerAccount.UnappliedCash)))
            .Distinct()
            .ToList();
        var creditNoteIds = postings
            .Where(posting => posting.DocumentType is LedgerDocumentType.CreditNote or LedgerDocumentType.CreditNoteAllocation)
            .Select(posting => new CreditNoteId(posting.DocumentType == LedgerDocumentType.CreditNote ? posting.DocumentId : SubjectOf(posting.Id, LedgerAccount.CreditAvailable)))
            .Distinct()
            .ToList();
        var adjustmentIds = postings
            .Where(posting => posting.DocumentType == LedgerDocumentType.Adjustment)
            .Select(posting => new AdjustmentId(posting.DocumentId))
            .Distinct()
            .ToList();
        var refundIds = postings
            .Where(posting => posting.DocumentType == LedgerDocumentType.Refund)
            .Select(posting => new RefundId(posting.DocumentId))
            .Distinct()
            .ToList();
        if (postings.FirstOrDefault(posting => !Enum.IsDefined(posting.DocumentType)) is { } unknown)
        {
            throw new InvalidOperationException($"Posting {unknown.Id} names a document of a type the export does not know: {unknown.DocumentType}.");
        }

        var bills = await context.Set<Bill>().AsNoTracking()
            .Where(bill => billIds.Contains(bill.Id))
            .Select(bill => new { bill.Id, bill.Number, bill.LegacyNumber, Tax = bill.TaxTotal.Amount })
            .ToDictionaryAsync(bill => bill.Id.Value, ct);
        var payments = await context.Set<Payment>().AsNoTracking()
            .Where(payment => paymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.Number, payment.Method })
            .ToDictionaryAsync(payment => payment.Id.Value, ct);
        var creditNotes = await context.Set<CreditNote>().AsNoTracking()
            .Where(creditNote => creditNoteIds.Contains(creditNote.Id))
            .Select(creditNote => new { creditNote.Id, creditNote.Number, Tax = creditNote.TaxTotal.Amount })
            .ToDictionaryAsync(creditNote => creditNote.Id.Value, ct);
        var adjustments = await context.Set<Adjustment>().AsNoTracking()
            .Where(adjustment => adjustmentIds.Contains(adjustment.Id))
            .Select(adjustment => new { adjustment.Id, adjustment.Number })
            .ToDictionaryAsync(adjustment => adjustment.Id.Value, ct);
        var refunds = await context.Set<Refund>().AsNoTracking()
            .Where(refund => refundIds.Contains(refund.Id))
            .Select(refund => new { refund.Id, refund.Number, refund.Method })
            .ToDictionaryAsync(refund => refund.Id.Value, ct);

        var payerIds = postings.Select(posting => posting.PayerId).Distinct().ToList();
        var payers = await context.Set<Payer>().AsNoTracking()
            .Where(payer => payerIds.Contains(payer.Id))
            .Select(payer => new { payer.Id, payer.Name })
            .ToDictionaryAsync(payer => payer.Id, payer => payer.Name, ct);

        var vouchers = postings.Select(posting =>
        {
            // The number of our document, the method its money came by, and the tax inside its revenue.
            string? number;
            PaymentMethod? method = null;
            var tax = 0m;
            switch (posting.DocumentType)
            {
                case LedgerDocumentType.Bill:
                    var bill = bills[posting.DocumentId];
                    number = bill.Number ?? bill.LegacyNumber;
                    tax = posting.Kind == LedgerPostingKind.BillPosted ? bill.Tax : 0m;
                    break;
                case LedgerDocumentType.Payment:
                    var payment = payments[posting.DocumentId];
                    (number, method) = (payment.Number, payment.Method);
                    break;
                case LedgerDocumentType.PaymentAllocation:
                    number = payments[SubjectOf(posting.Id, LedgerAccount.UnappliedCash)].Number;
                    break;
                case LedgerDocumentType.CreditNote:
                    var creditNote = creditNotes[posting.DocumentId];
                    number = creditNote.Number;
                    tax = posting.Kind == LedgerPostingKind.CreditNotePosted ? creditNote.Tax : 0m;
                    break;
                case LedgerDocumentType.CreditNoteAllocation:
                    number = creditNotes[SubjectOf(posting.Id, LedgerAccount.CreditAvailable)].Number;
                    break;
                case LedgerDocumentType.Adjustment:
                    number = adjustments[posting.DocumentId].Number;
                    break;
                default:
                    var refund = refunds[posting.DocumentId];
                    (number, method) = (refund.Number, refund.Method);
                    break;
            }

            var revenueTax = tax;
            var lines = linesOf[posting.Id]
                .SelectMany(entry => LinesOf(entry.Account, entry.Debit.Amount, entry.Credit.Amount, revenueTax, method, posting.Id))
                .ToList();

            return new ExportVoucher(
                posting.Id.Value,
                posting.BusinessDate,
                TypeOf(posting.Kind),
                NumberOf(posting.Kind, number ?? posting.DocumentId.ToString("N"), posting.Id),
                $"{posting.Description}. Payer: {payers[posting.PayerId]}.",
                lines);
        }).ToList();

        return new ExportVouchers(vouchers, moved.Sum(entry => entry.Debit.Amount), moved.Sum(entry => entry.Credit.Amount));
    }

    // The lines of one entry: itself, in the accountant's ledger; or, for the revenue of a bill or a credit note with
    // tax, its taxable value and its tax, which add up to it.
    private static IEnumerable<ExportVoucherLine> LinesOf(LedgerAccount account, decimal debit, decimal credit, decimal tax, PaymentMethod? method, LedgerPostingId posting)
    {
        switch (account)
        {
            case LedgerAccount.Revenue when tax > 0m && tax < debit + credit:
                // One of the two is zero: the tax is taken from the side the entry is on.
                yield return new ExportVoucherLine(ExportLedger.Revenue, debit == 0m ? 0m : debit - tax, credit == 0m ? 0m : credit - tax);
                yield return new ExportVoucherLine(ExportLedger.TaxOutput, debit == 0m ? 0m : tax, credit == 0m ? 0m : tax);
                break;
            case LedgerAccount.Cash:
                yield return new ExportVoucherLine(LedgerOf(method ?? throw new InvalidOperationException($"Posting {posting} moves cash and names no payment or refund.")), debit, credit);
                break;
            default:
                yield return new ExportVoucherLine(LedgerOf(account), debit, credit);
                break;
        }
    }

    private static ExportVoucherType TypeOf(LedgerPostingKind kind) => kind switch
    {
        LedgerPostingKind.PaymentRecorded => ExportVoucherType.Receipt,
        LedgerPostingKind.RefundPosted or LedgerPostingKind.PaymentReversed => ExportVoucherType.Payment,
        _ => ExportVoucherType.Journal,
    };

    // The document's own posting carries the document's number. A posting that follows from it (an allocation, an
    // application of credit, a reversal) carries the number with what it is and a mark made of its own id, so that no two
    // vouchers of a hospital share a number: a package may refuse a second one.
    private static string NumberOf(LedgerPostingKind kind, string number, LedgerPostingId posting)
    {
        var suffix = kind switch
        {
            LedgerPostingKind.PaymentAllocated => "AL",
            LedgerPostingKind.AllocationReversed => "AR",
            LedgerPostingKind.PaymentReversed => "RV",
            LedgerPostingKind.CreditApplied => "CA",
            LedgerPostingKind.CreditApplicationReversed => "CR",
            _ => null,
        };

        // Eight characters of a hash of the id, not of the id: ids made one after the other end alike.
        return suffix is null ? number : $"{number}/{suffix}-{Convert.ToHexStringLower(SHA256.HashData(posting.Value.ToByteArray()))[..8]}";
    }

    private static ExportLedger LedgerOf(LedgerAccount account) => account switch
    {
        LedgerAccount.Receivable => ExportLedger.Receivable,
        LedgerAccount.UnappliedCash => ExportLedger.UnappliedCash,
        LedgerAccount.CreditAvailable => ExportLedger.CreditAvailable,
        LedgerAccount.Revenue => ExportLedger.Revenue,
        LedgerAccount.Adjustments => ExportLedger.Adjustments,
        LedgerAccount.OpeningBalances => ExportLedger.OpeningBalances,
        _ => throw new InvalidOperationException($"The export has no ledger for the account {account}."),
    };

    private static ExportLedger LedgerOf(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => ExportLedger.Cash,
        PaymentMethod.Card => ExportLedger.Card,
        PaymentMethod.UPI => ExportLedger.Upi,
        PaymentMethod.BankTransfer => ExportLedger.BankTransfer,
        PaymentMethod.Cheque => ExportLedger.Cheque,
        _ => throw new InvalidOperationException($"The export has no ledger for the payment method {method}."),
    };
}
