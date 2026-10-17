namespace BillingRecon.Domain.Common;

/// <summary>
/// A mutable aggregate root guarded by optimistic concurrency (BR-804, ADR 0007). SQL Server changes the value on
/// every update of the row, and a save that started from a stale value fails. The implementing class declares the
/// property under the same name, with a non-public setter.
/// </summary>
public interface IHasRowVersion
{
    /// <summary>The <c>rowversion</c> of the row as last read. Empty until the entity has been saved.</summary>
    public byte[] RowVersion { get; }
}
