using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>A name is at most 100 characters and holds no control character.</summary>
public sealed class UpdateAccountingLedgersCommandValidator : AbstractValidator<UpdateAccountingLedgersCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateAccountingLedgersCommandValidator()
    {
        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.Names).Custom((names, context) =>
        {
            foreach (var ledger in Enum.GetValues<AccountingLedger>())
            {
                if (!AccountingLedgerNames.IsName(names.Of(ledger)))
                {
                    context.AddFailure(ledger.ToString(), $"A ledger's name has at most {AccountingLedgerNames.NameMaxLength} characters, on one line.");
                }
            }
        });
    }
}
