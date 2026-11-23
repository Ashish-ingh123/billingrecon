namespace BillingRecon.Application.Reporting;

/// <summary>
/// The whole reconciliation: when it was run, how many drift rows it found (zero when the caches equal the ledger and
/// every series is gapless), and how many for each broken identity.
/// </summary>
public sealed record ReconciliationTotalsDto(DateTimeOffset CheckedAtUtc, int Rows, IReadOnlyList<DriftCountDto> ByIdentity);
