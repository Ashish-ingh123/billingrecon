namespace BillingRecon.Api.Common.Correlation;

/// <summary>Gives the rest of the API access to the correlation id of the current request (BR-703).</summary>
public static class CorrelationIdHttpContextExtensions
{
    private static readonly object ItemKey = new();

    /// <summary>The correlation id of this request. <see cref="CorrelationIdMiddleware"/> sets it before anything else runs.</summary>
    public static Guid GetCorrelationId(this HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var value) && value is Guid correlationId)
        {
            return correlationId;
        }

        // Only reachable if the middleware is missing from the pipeline. Error handling calls this, so it must not throw.
        var generated = Guid.NewGuid();
        context.Items[ItemKey] = generated;

        return generated;
    }

    internal static void SetCorrelationId(this HttpContext context, Guid correlationId) => context.Items[ItemKey] = correlationId;
}
