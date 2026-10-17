namespace BillingRecon.Domain.Tests;

/// <summary>Placeholder until the first domain type arrives. Keeps the project runnable and the pipeline green.</summary>
public sealed class SmokeTests
{
    [Fact]
    public void Domain_assembly_is_discoverable()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("BillingRecon.Domain");
    }
}
