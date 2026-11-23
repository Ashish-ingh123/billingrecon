namespace BillingRecon.Application.Reporting;

/// <summary>
/// One business date of the dashboard, the points of the cards' sparklines (BR-902): what was billed and collected that
/// day, and at the end of the day what was owed on bills, what money was held unapplied, and how many requests waited.
/// </summary>
public sealed record DailyFiguresDto(DateOnly Date, decimal Billed, decimal Collected, decimal OutstandingDues, decimal UnappliedCash, int PendingApprovals);
