using BillingRecon.Domain.Audit;

namespace BillingRecon.Application.Audit;

/// <summary>
/// Appends audit records (BR-701). A record becomes part of the current unit of work: it is saved by the commit that
/// saves the change it describes, and rolled back with it. Nothing is written outside that transaction, and nothing
/// that was written is changed (BR-702).
/// </summary>
public interface IAuditWriter
{
    /// <summary>Adds <paramref name="record"/> to the unit of work.</summary>
    public void Append(AuditRecord record);
}
