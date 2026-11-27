using BillingRecon.Application.Receivables;


namespace BillingRecon.Infrastructure.Receivables;

/// <summary>What one nightly reconciliation found (ADR 0056).</summary>
/// <param name="Hospitals">How many hospitals were checked.</param>
/// <param name="Drift">The rows of drift, by hospital code; only the hospitals that have some.</param>
/// <param name="Failed">The codes of the hospitals that could not be reconciled.</param>
public sealed record ReconciliationNight(int Hospitals, IReadOnlyDictionary<string, IReadOnlyList<DriftRow>> Drift, IReadOnlyList<string> Failed);
