namespace BillingRecon.Domain.Common;

/// <summary>The id of one import of a bank's statement file (ADR 0065). In Common, like every typed id.</summary>
public readonly record struct StatementImportId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
