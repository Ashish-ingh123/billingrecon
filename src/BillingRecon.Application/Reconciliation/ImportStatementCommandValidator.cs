using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The shape of <see cref="ImportStatementCommand"/>: the shared rules and the dry run's file hash.</summary>
public sealed class ImportStatementCommandValidator : AbstractValidator<ImportStatementCommand>
{
    public ImportStatementCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        Include(new StatementFileInputValidator());
        RuleFor(command => command.ExpectedFileHash)
            .Must(hash => hash is { Length: StatementImport.FileHashLength } && hash.All(Uri.IsHexDigit))
            .WithMessage("The file hash of the dry run is required: run the dry run first and send its fileHash.");
    }
}
