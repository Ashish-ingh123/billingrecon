namespace BillingRecon.Application.Tests;

/// <summary>Placeholder until the first use case arrives. Keeps the project runnable and the pipeline green.</summary>
public sealed class SmokeTests
{
    [Fact]
    public void Application_assembly_is_discoverable()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("BillingRecon.Application");
    }
}
