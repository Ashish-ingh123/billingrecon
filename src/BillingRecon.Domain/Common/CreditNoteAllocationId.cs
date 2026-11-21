namespace BillingRecon.Domain.Common;

/// <summary>The id of the application of a credit note to a bill. In Common, like every typed id.</summary>
public readonly record struct CreditNoteAllocationId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
