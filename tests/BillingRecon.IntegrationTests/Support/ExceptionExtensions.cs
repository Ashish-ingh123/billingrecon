namespace BillingRecon.IntegrationTests.Support;

internal static class ExceptionExtensions
{
    /// <summary>The exception followed by its inner exceptions, outermost first.</summary>
    public static IEnumerable<Exception> WithInnerExceptions(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
