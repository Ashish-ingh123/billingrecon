using BillingRecon.Application.Billing;

namespace BillingRecon.Api.Billing;

/// <summary>Maps the lines of a request body to the lines a use case takes.</summary>
internal static class BillLineRequests
{
    /// <summary>
    /// The lines as the use case takes them; none when the body had no <c>lines</c>. A line sent as JSON <c>null</c>
    /// becomes an empty line, which the validator reports under its position.
    /// </summary>
    public static IReadOnlyList<BillLineInput> ToInputs(this IReadOnlyList<BillLineRequest?>? lines)
        => lines is null ? [] : [.. lines.Select(line => line?.ToInput() ?? new BillLineInput())];
}
