namespace BillingRecon.Application.Reporting;

/// <summary>
/// One card of the dashboard: its figure for the period, the same figure for the previous period, and the change in
/// percent (BR-902). <see cref="ChangePercent"/> is null when the previous figure is zero, and both are null for a figure
/// that has no history (reconciliation drift).
/// </summary>
public sealed record SummaryFigureDto(decimal Value, decimal? Previous, decimal? ChangePercent)
{
    /// <summary>A figure compared with its previous one.</summary>
    public static SummaryFigureDto Compared(decimal value, decimal previous) => new(value, previous, DashboardFigures.ChangePercent(value, previous));
}
