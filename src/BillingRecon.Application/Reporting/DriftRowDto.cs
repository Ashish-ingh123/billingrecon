namespace BillingRecon.Application.Reporting;

/// <summary>
/// One drift row of the reconciliation: which identity is broken (the names of docs/database.md section 5.6), the bill,
/// payment, credit note, payer, posting or number series it is about, its document number or other reference where it
/// has one, and the two figures that disagree: what is cached (or counted) and what the ledger (or the series) says.
/// </summary>
public sealed record DriftRowDto(string Identity, Guid SubjectId, string? Reference, decimal Cached, decimal Ledger);
