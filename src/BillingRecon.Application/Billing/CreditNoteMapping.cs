using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>Explicit mapping from the credit note aggregate to what the API returns (CLAUDE.md section 5).</summary>
public static class CreditNoteMapping
{
    /// <summary>The credit note as the API returns it, with its lines in the order of their numbers and its applications, the oldest first.</summary>
    public static CreditNoteDto ToDto(this CreditNote creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        return new CreditNoteDto(
            Id: creditNote.Id.Value,
            BillId: creditNote.BillId.Value,
            PayerId: creditNote.PayerId.Value,
            PatientId: creditNote.PatientId.Value,
            CreditNoteDate: creditNote.CreditNoteDate,
            Reason: creditNote.Reason,
            Status: creditNote.Status,
            Number: creditNote.Number,
            FiscalYear: creditNote.FiscalYear?.Label,
            PostedAtUtc: creditNote.PostedAtUtc,
            SubTotal: creditNote.SubTotal.Amount,
            DiscountTotal: creditNote.DiscountTotal.Amount,
            TaxTotal: creditNote.TaxTotal.Amount,
            Total: creditNote.Total.Amount,
            AvailableAmount: creditNote.AvailableAmount.Amount,
            VoidedAtUtc: creditNote.VoidedAtUtc,
            VoidReason: creditNote.VoidReason,
            Lines: [.. creditNote.Lines.OrderBy(line => line.LineNo).Select(ToDto)],
            Applications: [.. creditNote.Allocations.OrderBy(allocation => allocation.AllocatedAtUtc).ThenBy(allocation => allocation.Id.Value).Select(ToDto)],
            CreatedAtUtc: creditNote.CreatedAtUtc,
            UpdatedAtUtc: creditNote.UpdatedAtUtc,
            Version: RowVersionText.Of(creditNote));
    }

    /// <summary>The credit note as a list shows it.</summary>
    public static CreditNoteSummaryDto ToSummary(this CreditNote creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        return new CreditNoteSummaryDto(
            Id: creditNote.Id.Value,
            BillId: creditNote.BillId.Value,
            PayerId: creditNote.PayerId.Value,
            CreditNoteDate: creditNote.CreditNoteDate,
            Reason: creditNote.Reason,
            Status: creditNote.Status,
            Number: creditNote.Number,
            Total: creditNote.Total.Amount,
            AvailableAmount: creditNote.AvailableAmount.Amount,
            CreatedAtUtc: creditNote.CreatedAtUtc,
            UpdatedAtUtc: creditNote.UpdatedAtUtc,
            Version: RowVersionText.Of(creditNote));
    }

    /// <summary>One line of a credit note as the API returns it.</summary>
    public static CreditNoteLineDto ToDto(this CreditNoteLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new CreditNoteLineDto(
            LineNo: line.LineNo,
            BillLineId: line.BillLineId?.Value,
            Description: line.Description,
            Quantity: line.Quantity.Value,
            UnitPrice: line.UnitPrice.Value,
            LineAmount: line.LineAmount.Amount,
            DiscountAmount: line.DiscountAmount.Amount,
            TaxRatePercent: line.TaxRate.Value,
            TaxAmount: line.TaxAmount.Amount,
            LineTotal: line.LineTotal.Amount);
    }

    /// <summary>One application of credit as the API returns it.</summary>
    public static CreditNoteApplicationDto ToDto(this CreditNoteAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        return new CreditNoteApplicationDto(
            Id: allocation.Id.Value,
            BillId: allocation.BillId.Value,
            Amount: allocation.Amount.Amount,
            Status: allocation.Status,
            AllocatedAtUtc: allocation.AllocatedAtUtc,
            AllocatedByUserId: allocation.AllocatedByUserId?.Value);
    }
}
