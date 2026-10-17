namespace BillingRecon.Domain.Common;

/// <summary>
/// Base type of everything that has an identity. The id is typed (<typeparamref name="TId"/>), so a payment id
/// cannot be used where a bill id is expected.
/// </summary>
/// <typeparam name="TId">The typed id of this kind of entity.</typeparam>
public abstract class Entity<TId>
    where TId : struct, ITypedId
{
    /// <summary>
    /// Identity. A new entity has the default (empty) id. The persistence layer assigns a sequential value at the
    /// moment the entity is added to the unit of work, not when it is saved (docs/database.md, section 1), so the
    /// id is known before the transaction commits. Application code never assigns it.
    /// </summary>
    public TId Id { get; private set; }
}
