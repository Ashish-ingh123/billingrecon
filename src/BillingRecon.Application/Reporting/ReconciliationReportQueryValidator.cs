using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The shape of <see cref="ReconciliationReportQuery"/>: a page within the limits.</summary>
public sealed class ReconciliationReportQueryValidator : AbstractValidator<ReconciliationReportQuery>
{
    public ReconciliationReportQueryValidator()
    {
        Include(new PagedQueryValidator());
    }
}
