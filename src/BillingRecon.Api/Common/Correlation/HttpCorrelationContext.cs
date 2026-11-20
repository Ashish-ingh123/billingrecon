using BillingRecon.Application.Common;

namespace BillingRecon.Api.Common.Correlation;

/// <summary>
/// The correlation id of the current scope (BR-703): in a request, the id <see cref="CorrelationIdMiddleware"/> gave
/// it, the one in the response header and in every log line; outside a request (a hosted service, a scope a test
/// opens), an id generated once for the scope. One instance per scope.
/// </summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    private Guid? _generated;

    /// <inheritdoc />
    public Guid CorrelationId => accessor.HttpContext?.GetCorrelationId() ?? (_generated ??= Guid.NewGuid());
}
