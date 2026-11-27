using BillingRecon.Infrastructure.Common.Operations;
using BillingRecon.Infrastructure.Receivables;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// When the nightly reconciliation runs (ADR 0056): at 02:00 in India, the next one after now; and the address of the
/// alerts is checked at startup.
/// </summary>
public sealed class OperationsScheduleTests
{
    [Theory]
    [InlineData("2026-11-27T10:00:00+00:00", "2026-11-27T20:30:00+00:00")]
    [InlineData("2026-11-27T20:29:59+00:00", "2026-11-27T20:30:00+00:00")]
    [InlineData("2026-11-27T20:30:00+00:00", "2026-11-28T20:30:00+00:00")]
    [InlineData("2026-11-27T19:00:00+00:00", "2026-11-27T20:30:00+00:00")]
    [InlineData("2026-12-31T21:00:00+00:00", "2027-01-01T20:30:00+00:00")]
    public void ADR0056_the_reconciliation_runs_next_at_two_in_the_morning_in_india(string now, string next)
    {
        // 02:00 IST is 20:30 UTC of the day before.
        NightlyReconciler.NextRunAfter(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateTimeOffset.Parse(next, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("oncall@hospital.example", true)]
    [InlineData("not an address", false)]
    public void ADR0056_the_address_of_the_alerts_is_one_or_none(string mailTo, bool usable)
        => AlertOptions.IsUsable(new AlertOptions { MailTo = mailTo }).ShouldBe(usable);
}
