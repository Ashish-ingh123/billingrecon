namespace BillingRecon.Domain.Common;

/// <summary>The id of a hospital's print settings for one kind of document (ADR 0059).</summary>
public readonly record struct PrintSettingsId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
