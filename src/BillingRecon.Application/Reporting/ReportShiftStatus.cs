namespace BillingRecon.Application.Reporting;

/// <summary>Where a shift stands, in the words of Reporting (ADR 0036: Reporting has its own words for the enums of other modules).</summary>
public enum ReportShiftStatus
{
    /// <summary>Open: cash is still taken in it.</summary>
    Open = 0,

    /// <summary>Counted with a difference, waiting for a checker.</summary>
    PendingApproval = 1,

    /// <summary>Closed.</summary>
    Closed = 2,
}
