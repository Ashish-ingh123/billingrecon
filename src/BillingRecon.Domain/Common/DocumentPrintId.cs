namespace BillingRecon.Domain.Common;

/// <summary>The id of the record of one print of a document (ADR 0054).</summary>
public readonly record struct DocumentPrintId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
