namespace BillingRecon.Application.Reporting;

/// <summary>
/// One shift of the day-end report (BR-903): its cash from the ledger, what its drawer should hold, and, once it is
/// counted, what it held and the difference.
/// </summary>
public sealed record DayEndShiftDto(
    Guid ShiftId,
    Guid CashierUserId,
    string? CashierName,
    ReportShiftStatus Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    decimal OpeningFloat,
    decimal Received,
    decimal PaidOut,
    decimal Net,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Difference);
