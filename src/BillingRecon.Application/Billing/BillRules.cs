using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The rules the validators of the bill commands share: the lines, and the two texts of the header.</summary>
internal static class BillRules
{
    /// <summary>At most <see cref="Bill.MaxLines"/> lines, each of them valid; the errors are keyed <c>lines[0].quantity</c>.</summary>
    public static void Lines<T>(this AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, IEnumerable<BillLineInput>>> lines)
    {
        validator.RuleFor(lines)
            .Must(sent => sent.Count() <= Bill.MaxLines)
            .WithMessage($"A bill has at most {Bill.MaxLines} lines.");

        // Every line is checked, not only the lines before the first invalid one: a clerk corrects them in one go.
        validator.RuleForEach(lines)
            .Cascade(CascadeMode.Continue)
            .NotNull().WithMessage("A line cannot be null.")
            .SetValidator(new BillLineInputValidator());
    }

    /// <summary>The visit or admission the bill is for: optional, of a bounded length.</summary>
    public static IRuleBuilderOptions<T, string?> EncounterReference<T>(this IRuleBuilder<T, string?> rule)
        => rule.OptionalText(Bill.EncounterReferenceMaxLength, "encounter reference");

    /// <summary>The note on the bill: optional, of a bounded length.</summary>
    public static IRuleBuilderOptions<T, string?> Notes<T>(this IRuleBuilder<T, string?> rule)
        => rule.OptionalText(Bill.NotesMaxLength, "notes");
}
