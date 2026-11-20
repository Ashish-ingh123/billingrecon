namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Says who ends the transaction of the current request. Normally the unit of work does: it saves, and commits a
/// transaction the number series opened (ADR 0008). While the idempotency store holds the transaction (ADR 0006), the
/// unit of work only saves into it, and the store commits it together with the stored answer, or rolls it back.
/// One instance per request scope.
/// </summary>
public sealed class RequestTransaction
{
    /// <summary>True while the idempotency store holds the transaction of the request.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>The idempotency store has opened the transaction and will end it.</summary>
    public void Hold() => IsHeld = true;

    /// <summary>The idempotency store has ended the transaction.</summary>
    public void Release() => IsHeld = false;
}
