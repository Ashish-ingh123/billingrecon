using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The shape of <see cref="AllocatePaymentCommand"/>: the version the client read, between one and
/// <see cref="AllocatePaymentCommand.MaxAllocations"/> lines, no bill named twice, and for every line a bill and an
/// amount above zero that is exact to the paisa. The caps of BR-303 are business rules and are checked by the two
/// aggregates.
/// </summary>
public sealed class AllocatePaymentCommandValidator : AbstractValidator<AllocatePaymentCommand>
{
    public AllocatePaymentCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");

        RuleFor(command => command.Allocations)
            .Must(allocations => allocations.Count >= 1)
            .WithMessage("At least one allocation is required.")
            .Must(allocations => allocations.Count <= AllocatePaymentCommand.MaxAllocations)
            .WithMessage($"One request allocates to at most {AllocatePaymentCommand.MaxAllocations} bills.")
            .Must(NameEveryBillOnce)
            .WithMessage("A bill is named once in a request; add the amounts for the same bill together.");

        // Every line is checked, not only the lines before the first invalid one: a cashier corrects them in one go.
        RuleForEach(command => command.Allocations)
            .Cascade(CascadeMode.Continue)
            .NotNull().WithMessage("An allocation cannot be null.")
            .ChildRules(allocation =>
            {
                allocation.RuleFor(line => line.BillId)
                    .Must(billId => billId.Value != Guid.Empty)
                    .WithMessage("The bill id is required.");

                allocation.RuleFor(line => line.Amount)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("The amount is required.")
                    .Must(amount => amount is { } value && value > 0m && value <= Payment.MaxAmount && Money.TryOf(value, out _))
                    .WithMessage($"The amount is more than 0 and at most 9999999999.99, with at most {Money.Scale} decimals.");
            });
    }

    // Lines without a bill are refused by their own rule; here only the bills that are named count.
    private static bool NameEveryBillOnce(IReadOnlyList<AllocationInput> allocations)
    {
        var named = allocations.Where(allocation => allocation is not null && allocation.BillId.Value != Guid.Empty).Select(allocation => allocation.BillId).ToArray();

        return named.Distinct().Count() == named.Length;
    }
}
