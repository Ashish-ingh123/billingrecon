using BillingRecon.Application.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>The rules shared by the dry run and the import: a channel, a file name and some text; how big the text may be is the reader's.</summary>
internal sealed class StatementFileInputValidator : AbstractValidator<IStatementFileInput>
{
    public StatementFileInputValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(input => input.ChannelId).NotEmpty().WithMessage("The channel is required.");
        RuleFor(input => input.FileName).RequiredText(StatementImport.FileNameMaxLength, "file name");
        RuleFor(input => input.Text).NotEmpty().WithMessage("The file is empty.");
    }
}
