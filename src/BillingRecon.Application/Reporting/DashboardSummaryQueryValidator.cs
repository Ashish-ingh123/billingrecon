using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="DashboardSummaryQuery"/>: a period of the dashboard (BR-902).</summary>
public sealed class DashboardSummaryQueryValidator : AbstractValidator<DashboardSummaryQuery>
{
    public DashboardSummaryQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        this.AddPeriodRules(query => query.From, query => query.To);
    }
}
