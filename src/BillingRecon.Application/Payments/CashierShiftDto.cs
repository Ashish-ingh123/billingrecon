using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A shift as the API answers it (ADR 0060). <see cref="CashReceived"/> and <see cref="CashPaidOut"/> are read from
/// the ledger when the shift is read, and <see cref="ExpectedCash"/> is the float and their difference (BR-312), so
/// an open shift shows what its drawer should hold now. The count and what follows from it are null until the shift
/// is counted.
/// </summary>
public sealed record CashierShiftDto(
    Guid Id,
    Guid CashierUserId,
    string? CashierName,
    DateOnly BusinessDate,
    CashierShiftStatus Status,
    decimal OpeningFloat,
    DateTimeOffset OpenedAtUtc,
    int CashDocuments,
    decimal CashReceived,
    decimal CashPaidOut,
    decimal ExpectedCash,
    IReadOnlyList<CashCountLineDto>? Count,
    decimal? LooseChange,
    decimal? CountedCash,
    decimal? Difference,
    string? DifferenceReason,
    DateTimeOffset? CountedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string Version);
