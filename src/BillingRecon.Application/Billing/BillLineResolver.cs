using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Turns the lines a client sent into the lines a bill takes (BR-201). A line that names a service item takes from it
/// whatever the client left out: the description, the unit price, the tax rate. The item is read through the
/// catalogue's own read service, in one query for all lines; the line keeps its own copy, so the catalogue can change
/// later without altering the bill.
/// </summary>
internal static class BillLineResolver
{
    /// <summary>
    /// The lines for the aggregate, or the first failure: <c>service_item_not_found</c> for an item the tenant does
    /// not have, <c>service_item_inactive</c> for an inactive one, unless it is among <paramref name="alreadyOnBill"/>:
    /// a line the draft already has may stay when its item was deactivated since.
    /// </summary>
    /// <param name="items">The catalogue.</param>
    /// <param name="inputs">The lines of a validated command.</param>
    /// <param name="alreadyOnBill">The service items the bill's current lines name; empty for a new bill.</param>
    /// <param name="ct">Cancels the read.</param>
    public static async Task<Result<IReadOnlyList<BillLineDetails>>> ResolveAsync(
        IServiceItemReadService items,
        IReadOnlyList<BillLineInput> inputs,
        IReadOnlySet<ServiceItemId> alreadyOnBill,
        CancellationToken ct)
    {
        var ids = inputs.Select(input => input.ServiceItemId).OfType<ServiceItemId>().Distinct().ToArray();
        var catalogue = ids.Length == 0
            ? []
            : (await items.GetManyAsync(ids, ct)).ToDictionary(item => new ServiceItemId(item.Id));

        var lines = new List<BillLineDetails>(inputs.Count);
        foreach (var input in inputs)
        {
            ServiceItemDto? item = null;
            if (input.ServiceItemId is { } itemId)
            {
                if (!catalogue.TryGetValue(itemId, out item))
                {
                    return BillingErrors.ServiceItemNotFound();
                }

                if (item.Status != ServiceItemStatus.Active && !alreadyOnBill.Contains(itemId))
                {
                    return BillingErrors.ServiceItemInactive();
                }
            }

            lines.Add(new BillLineDetails
            {
                ServiceItemId = input.ServiceItemId,
                Description = string.IsNullOrWhiteSpace(input.Description) ? item?.Name ?? string.Empty : input.Description,
                Quantity = Quantity.Of(Required(input.Quantity, "quantity")),
                UnitPrice = UnitPrice.Of(input.UnitPrice ?? item?.DefaultUnitPrice ?? Required(null, "unit price")),
                DiscountAmount = Money.Of(input.DiscountAmount ?? 0m),
                TaxRate = Percent.Of(input.TaxRatePercent ?? item?.TaxRatePercent ?? Required(null, "tax rate")),
            });
        }

        return lines;
    }

    // The validator refuses a line without these, so no request takes this branch.
    private static decimal Required(decimal? value, string name)
        => value ?? throw new ArgumentException($"A bill line has no {name}; the validator of the use case should have refused it.");
}
