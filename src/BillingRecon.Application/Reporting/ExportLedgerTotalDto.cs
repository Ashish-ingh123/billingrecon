
namespace BillingRecon.Application.Reporting;

/// <summary>What one ledger of the accountant's books receives in a period.</summary>
/// <param name="Ledger">The ledger.</param>
/// <param name="Name">The name the hospital gave it, or null when none is set.</param>
/// <param name="Debit">The debits.</param>
/// <param name="Credit">The credits.</param>
public sealed record ExportLedgerTotalDto(ExportLedger Ledger, string? Name, decimal Debit, decimal Credit);
