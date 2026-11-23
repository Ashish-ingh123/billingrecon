namespace BillingRecon.Application.Reporting;

/// <summary>
/// The arithmetic of the dashboard that is not money (BR-902): a change in percent and a share in percent, each rounded
/// to two decimal places, half away from zero, like money (CLAUDE.md section 4, rule 1). Amounts themselves are never
/// rounded here: they are sums of amounts already rounded at their documents.
/// </summary>
public static class DashboardFigures
{
    /// <summary>
    /// The change from <paramref name="previous"/> to <paramref name="current"/> in percent of <paramref name="previous"/>;
    /// null when <paramref name="previous"/> is zero, since a change from nothing has no percentage.
    /// </summary>
    public static decimal? ChangePercent(decimal current, decimal previous)
        => previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary><paramref name="part"/> in percent of <paramref name="whole"/>; zero when the whole is zero.</summary>
    public static decimal Share(decimal part, decimal whole)
        => whole == 0m ? 0m : Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);
}
