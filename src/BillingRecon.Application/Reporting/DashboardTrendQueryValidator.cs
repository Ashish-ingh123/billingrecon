using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="DashboardTrendQuery"/>: a period of the dashboard and a known interval (BR-902).</summary>
public sealed class DashboardTrendQueryValidator : AbstractValidator<DashboardTrendQuery>
{
    public DashboardTrendQueryValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        this.AddPeriodRules(query => query.From, query => query.To);

        RuleFor(query => query.Interval).IsInEnum().WithMessage("The interval is Day or Week.");
    }
}
