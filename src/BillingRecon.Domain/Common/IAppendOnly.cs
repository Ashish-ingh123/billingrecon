namespace BillingRecon.Domain.Common;

/// <summary>
/// Marks an entity whose rows are written once and never again: audit records (BR-702) and, with the receivables
/// ledger, ledger postings and entries (CLAUDE.md section 4, rules 2 and 11). The persistence layer refuses a save
/// that would update or delete a row of an entity that carries this marker. A correction is a new row.
/// </summary>
public interface IAppendOnly
{
}
