namespace BillingRecon.Domain.Common;

/// <summary>The id of an approval request.</summary>
public readonly record struct ApprovalRequestId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
