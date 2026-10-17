namespace BillingRecon.Domain.Common;

/// <summary>
/// A typed identifier: a <see cref="Guid"/> wrapped in its own type, so that one kind of id cannot be passed where
/// another is expected (docs/domain-model.md, section 3). Every typed id is declared as
/// <c>readonly record struct XId(Guid Value) : ITypedId</c>; the persistence layer finds the types through this
/// interface and stores the plain <c>uniqueidentifier</c>.
/// </summary>
public interface ITypedId
{
    /// <summary>The underlying value. <see cref="Guid.Empty"/> means "not assigned yet".</summary>
    public Guid Value { get; }
}
