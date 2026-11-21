namespace BillingRecon.Domain.Common;

/// <summary>The id of a credit note. In Common, like every typed id.</summary>
public readonly record struct CreditNoteId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
