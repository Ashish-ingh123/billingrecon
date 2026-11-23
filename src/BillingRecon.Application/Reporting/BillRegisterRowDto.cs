namespace BillingRecon.Application.Reporting;

/// <summary>One posted bill in the bill register.</summary>
public sealed record BillRegisterRowDto(
    Guid BillId,
    string Number,
    DateOnly BillDate,
    DateOnly DueDate,
    Guid PatientId,
    string PatientName,
    Guid PayerId,
    string PayerName,
    decimal Total,
    decimal BalanceDue,
    ReportBillStatus Status,
    DateTimeOffset PostedAtUtc);
