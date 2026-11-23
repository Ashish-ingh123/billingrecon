using System.Linq.Expressions;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The rules of a register's period, shared by the validators of the registers (<see cref="ReportPeriods"/>).</summary>
internal static class ReportPeriodRules
{
    /// <summary>Both ends or neither, the end not before the start, at most <see cref="ReportPeriods.MaxDays"/> days.</summary>
    public static void AddPeriodRules<T>(this AbstractValidator<T> validator, Func<T, DateOnly?> from, Expression<Func<T, DateOnly?>> to)
    {
        validator.RuleFor(to)
            .Must((query, end) => (from(query) is null) == (end is null))
            .WithMessage("Give both from and to, or neither for the current month.")
            .Must((query, end) => end is null || from(query) is not { } start || start <= end)
            .WithMessage("The end of the period is not before its start.")
            .Must((query, end) => end is null || from(query) is not { } start || end.Value.DayNumber - start.DayNumber < ReportPeriods.MaxDays)
            .WithMessage($"A period is at most {ReportPeriods.MaxDays} days.");
    }
}
