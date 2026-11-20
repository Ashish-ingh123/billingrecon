using BillingRecon.Application.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>The correlation id of a test scope that has no request around it (BR-703).</summary>
public sealed class FixedCorrelation(Guid correlationId) : ICorrelationContext
{
    /// <inheritdoc />
    public Guid CorrelationId { get; } = correlationId;
}
