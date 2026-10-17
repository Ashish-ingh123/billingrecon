namespace BillingRecon.IntegrationTests.Support;

/// <summary>Body of the test-only echo endpoint: one required member, so a missing or malformed value fails to bind.</summary>
public sealed record EchoRequest
{
    public required decimal Amount { get; init; }
}
