using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Explicit mapping between the Billing aggregates and what the Application layer takes in and hands out (CLAUDE.md
/// section 5): the details of a command become the details an aggregate takes, and an aggregate becomes its DTO.
/// </summary>
public static class BillingMapping
{
    /// <summary>The service item as the API returns it.</summary>
    public static ServiceItemDto ToDto(this ServiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ServiceItemDto(
            Id: item.Id.Value,
            Code: item.Code.Value,
            Name: item.Name,
            Category: item.Category,
            DefaultUnitPrice: item.DefaultUnitPrice.Value,
            TaxRatePercent: item.TaxRate.Value,
            Status: item.Status,
            CreatedAtUtc: item.CreatedAtUtc,
            UpdatedAtUtc: item.UpdatedAtUtc,
            Version: RowVersionText.Of(item));
    }

    /// <summary>The bill as the API returns it, with its lines in the order of their numbers.</summary>
    public static BillDto ToDto(this Bill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);

        return new BillDto(
            Id: bill.Id.Value,
            PatientId: bill.PatientId.Value,
            PayerId: bill.PayerId.Value,
            BillDate: bill.BillDate,
            DueDate: bill.DueDate,
            EncounterReference: bill.EncounterReference,
            Notes: bill.Notes,
            Status: bill.Status,
            Number: bill.Number,
            FiscalYear: bill.FiscalYear?.Label,
            PostedAtUtc: bill.PostedAtUtc,
            SubTotal: bill.SubTotal.Amount,
            DiscountTotal: bill.DiscountTotal.Amount,
            TaxTotal: bill.TaxTotal.Amount,
            Total: bill.Total.Amount,
            BalanceDue: bill.BalanceDue.Amount,
            VoidedAtUtc: bill.VoidedAtUtc,
            VoidReason: bill.VoidReason,
            Lines: [.. bill.Lines.OrderBy(line => line.LineNo).Select(ToDto)],
            CreatedAtUtc: bill.CreatedAtUtc,
            UpdatedAtUtc: bill.UpdatedAtUtc,
            Version: RowVersionText.Of(bill));
    }

    /// <summary>The bill as a list shows it, without its lines.</summary>
    public static BillSummaryDto ToSummary(this Bill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);

        return new BillSummaryDto(
            Id: bill.Id.Value,
            PatientId: bill.PatientId.Value,
            PayerId: bill.PayerId.Value,
            BillDate: bill.BillDate,
            DueDate: bill.DueDate,
            EncounterReference: bill.EncounterReference,
            Status: bill.Status,
            Number: bill.Number,
            Total: bill.Total.Amount,
            BalanceDue: bill.BalanceDue.Amount,
            CreatedAtUtc: bill.CreatedAtUtc,
            UpdatedAtUtc: bill.UpdatedAtUtc,
            Version: RowVersionText.Of(bill));
    }

    /// <summary>One line of a bill as the API returns it.</summary>
    public static BillLineDto ToDto(this BillLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new BillLineDto(
            LineNo: line.LineNo,
            ServiceItemId: line.ServiceItemId?.Value,
            Description: line.Description,
            Quantity: line.Quantity.Value,
            UnitPrice: line.UnitPrice.Value,
            LineAmount: line.LineAmount.Amount,
            DiscountAmount: line.DiscountAmount.Amount,
            TaxRatePercent: line.TaxRate.Value,
            TaxAmount: line.TaxAmount.Amount,
            LineTotal: line.LineTotal.Amount);
    }

    /// <summary>
    /// The details of a validated command, as the service item takes them. The validator has refused a command without
    /// a price or a tax rate, and one whose numbers the value objects do not accept, so a failure here is a bug.
    /// </summary>
    /// <exception cref="ArgumentException">The command was not validated: a number is missing or out of range.</exception>
    public static ServiceItemDetails ToDetails(this IServiceItemDetailsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.DefaultUnitPrice is not { } price || input.TaxRatePercent is not { } taxRate)
        {
            throw new ArgumentException("The command has no price or no tax rate; its validator should have refused it.", nameof(input));
        }

        return new ServiceItemDetails
        {
            Name = input.Name,
            Category = input.Category,
            DefaultUnitPrice = UnitPrice.Of(price),
            TaxRate = Percent.Of(taxRate),
        };
    }
}
