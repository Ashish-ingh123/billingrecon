using NetArchTest.Rules;

namespace BillingRecon.ArchitectureTests;

internal static class TestResultExtensions
{
    public static void ShouldPass(this TestResult result)
    {
        var violations = string.Join(", ", result.FailingTypeNames ?? []);

        result.IsSuccessful.ShouldBeTrue($"Violating types: {violations}");
    }
}
