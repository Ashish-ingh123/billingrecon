using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>The period of an export: both dates, in order, at most 92 days.</summary>
public static class AccountingExportPeriod
{
    /// <summary>Longest period of one file: a quarter.</summary>
    public const int MaxDays = 92;

    /// <summary>The refusal of a period that is not one, or null.</summary>
    public static Error? Check(DateOnly? from, DateOnly? to)
    {
        var problems = new Dictionary<string, string[]>();
        if (from is null)
        {
            problems["from"] = ["Give the first business date of the period."];
        }

        if (to is null)
        {
            problems["to"] = ["Give the last business date of the period."];
        }
        else if (from is { } start && to.Value < start)
        {
            problems["to"] = ["The end of the period is not before its start."];
        }
        else if (from is { } first && to.Value.DayNumber - first.DayNumber >= MaxDays)
        {
            problems["to"] = [$"A period is at most {MaxDays} days."];
        }

        return problems.Count == 0 ? null : CommonErrors.ValidationFailed(problems);
    }
}
