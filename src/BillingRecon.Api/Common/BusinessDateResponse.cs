namespace BillingRecon.Api.Common;

/// <summary>
/// The business date of the server (Prompt 12.2, ADR 0063): today in India (BR-003), from the server's clock and never
/// the browser's, so that "Today" and "This month" of a list's date filter are the server's days.
/// </summary>
/// <param name="Today">Today in India, as <c>yyyy-MM-dd</c>.</param>
public sealed record BusinessDateResponse(DateOnly Today);
