namespace BillingRecon.Application.Common;

/// <summary>
/// The correlation id of the current unit of work (BR-703). In a request it is the id the API accepted from
/// <c>X-Correlation-Id</c> or generated, the one in the response header and in every log line of the request. Work
/// that no request started has an id of its own, one per scope.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>The correlation id. Never empty.</summary>
    public Guid CorrelationId { get; }
}
