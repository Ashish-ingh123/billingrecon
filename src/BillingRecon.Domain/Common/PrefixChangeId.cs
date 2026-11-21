namespace BillingRecon.Domain.Common;

/// <summary>The id of a change of a document-number prefix. In Common, like every typed id.</summary>
public readonly record struct PrefixChangeId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
