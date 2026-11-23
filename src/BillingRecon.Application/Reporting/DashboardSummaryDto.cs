namespace BillingRecon.Application.Reporting;

/// <summary>
/// The summary of the dashboard (US-44, BR-902): the period and the previous one, and six cards. Billed and collected are
/// the sums of each period; outstanding dues, unapplied cash and pending approvals are those at the end of each period's
/// last day; reconciliation drift is the count of now, with no previous figure.
/// </summary>
public sealed record DashboardSummaryDto(
    ReportPeriodDto Period,
    ReportPeriodDto PreviousPeriod,
    SummaryFigureDto TotalBilled,
    SummaryFigureDto AmountCollected,
    SummaryFigureDto OutstandingDues,
    SummaryFigureDto UnappliedCash,
    SummaryFigureDto PendingApprovals,
    SummaryFigureDto ReconciliationDrift);
