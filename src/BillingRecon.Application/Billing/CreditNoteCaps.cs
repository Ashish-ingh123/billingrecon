using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The caps of BR-210 and BR-211, measured against what the database holds at the moment of the call: the posted
/// credit notes of the bill, other than the one being checked. The rule itself is
/// <see cref="CreditNote.CheckWithinBill"/>.
/// </summary>
internal static class CreditNoteCaps
{
    /// <summary>Success when the credits of <paramref name="bill"/> stay within it with <paramref name="creditNote"/>.</summary>
    public static async Task<Result> CheckAsync(ICreditNoteRepository repository, CreditNote creditNote, Bill bill, CancellationToken ct)
    {
        var posted = (await repository.ListPostedForBillAsync(bill.Id, ct)).Where(other => other.Id != creditNote.Id).ToArray();

        var alreadyCredited = posted.Select(other => other.Total).Sum();
        var alreadyCreditedByLine = posted
            .SelectMany(other => other.Lines)
            .Where(line => line.BillLineId is not null)
            .GroupBy(line => line.BillLineId!.Value)
            .ToDictionary(lines => lines.Key, lines => lines.Select(line => line.LineTotal).Sum());

        return creditNote.CheckWithinBill(bill.Total, alreadyCredited, bill.Lines.ToDictionary(line => line.Id, line => line.LineTotal), alreadyCreditedByLine);
    }
}
