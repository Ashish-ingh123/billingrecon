using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Turns the lines a client sent into the lines a credit note takes (BR-211). A line that names a line of the bill
/// takes from it whatever the client left out: the description, the unit price, the tax rate. The credit note keeps
/// its own copy.
/// </summary>
internal static class CreditNoteLineResolver
{
    /// <summary>
    /// The lines for the aggregate, or <c>credit_note_bill_line_not_found</c> for the first line that names a line
    /// <paramref name="bill"/> does not have.
    /// </summary>
    public static Result<IReadOnlyList<CreditNoteLineDetails>> Resolve(Bill bill, IReadOnlyList<CreditNoteLineInput> inputs)
    {
        var billLines = bill.Lines.ToDictionary(line => line.Id);
        var lines = new List<CreditNoteLineDetails>(inputs.Count);

        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            BillLine? billLine = null;
            if (input.BillLineId is { } billLineId && !billLines.TryGetValue(billLineId, out billLine))
            {
                return BillingErrors.CreditNoteBillLineNotFound(index + 1);
            }

            lines.Add(new CreditNoteLineDetails
            {
                BillLineId = input.BillLineId,
                Description = string.IsNullOrWhiteSpace(input.Description) ? billLine?.Description ?? string.Empty : input.Description,
                Quantity = Quantity.Of(Required(input.Quantity, "quantity")),
                UnitPrice = input.UnitPrice is { } price ? UnitPrice.Of(price) : billLine?.UnitPrice ?? UnitPrice.Of(Required(null, "unit price")),
                DiscountAmount = Money.Of(input.DiscountAmount ?? 0m),
                TaxRate = input.TaxRatePercent is { } taxRate ? Percent.Of(taxRate) : billLine?.TaxRate ?? Percent.Of(Required(null, "tax rate")),
            });
        }

        return lines;
    }

    // The validator refuses a line without these, so no request takes this branch.
    private static decimal Required(decimal? value, string name)
        => value ?? throw new ArgumentException($"A credit note line has no {name}; the validator of the use case should have refused it.");
}
