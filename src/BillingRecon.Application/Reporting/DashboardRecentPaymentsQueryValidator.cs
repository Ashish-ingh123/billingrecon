using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="DashboardRecentPaymentsQuery"/>: a period of the dashboard (BR-902).</summary>
public sealed class DashboardRecentPaymentsQueryValidator : AbstractValidator<DashboardRecentPaymentsQuery>
{
    public DashboardRecentPaymentsQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        this.AddPeriodRules(query => query.From, query => query.To);
    }
}
