using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The rules the validators of the credit note commands share: the lines and the reason.</summary>
internal static class CreditNoteRules
{
    /// <summary>At most <see cref="CreditNote.MaxLines"/> lines, each of them valid; the errors are keyed <c>lines[0].quantity</c>.</summary>
    public static void CreditNoteLines<T>(this AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, IEnumerable<CreditNoteLineInput>>> lines)
    {
        validator.RuleFor(lines)
            .Must(sent => sent.Count() <= CreditNote.MaxLines)
            .WithMessage($"A credit note has at most {CreditNote.MaxLines} lines.");

        // Every line is checked, not only the lines before the first invalid one: a clerk corrects them in one go.
        validator.RuleForEach(lines)
            .Cascade(CascadeMode.Continue)
            .NotNull().WithMessage("A line cannot be null.")
            .SetValidator(new CreditNoteLineInputValidator());
    }

    /// <summary>Why the credit is given, or why the draft is voided: required, of a bounded length.</summary>
    public static IRuleBuilderOptions<T, string> CreditNoteReason<T>(this IRuleBuilder<T, string> rule)
        => rule.RequiredText(CreditNote.ReasonMaxLength, "reason");
}
