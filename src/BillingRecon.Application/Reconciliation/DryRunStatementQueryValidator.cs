using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="DryRunStatementQuery"/>: a channel, a file name and some text.</summary>
public sealed class DryRunStatementQueryValidator : AbstractValidator<DryRunStatementQuery>
{
    public DryRunStatementQueryValidator() => Include(new StatementFileInputValidator());
}
