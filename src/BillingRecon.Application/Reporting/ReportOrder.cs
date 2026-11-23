namespace BillingRecon.Application.Reporting;

/// <summary>The order of a register's items: chronological, the default, or the newest first (the dashboard's recent payments).</summary>
public enum ReportOrder
{
    /// <summary>By date, then by number, the oldest first.</summary>
    OldestFirst = 0,

    /// <summary>By date, then by number, the newest first.</summary>
    NewestFirst = 1,
}
