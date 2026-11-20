using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Allocates a payment to posted bills of its payer (US-31; BR-303, BR-304, BR-208). This is the use case that
/// changes two aggregates at once (docs/domain-model.md, section 6): for every line of the request the payment's
/// unapplied amount and the bill's balance due fall by the same amount, the ledger gets the pair
/// Dr UnappliedCash(payment) / Cr Receivable(bill), the payer's two cached balances fall with it, and one commit
/// saves all of it with the audit records, or none of it (BR-007). The payment reaches a bill only through
/// <see cref="IBillSettlement"/>, the Billing module's contract.
/// <para>
/// Order and locks (ADR 0024). First everything that can refuse, with nothing locked: the shape of the request, the
/// payment and the version the client read (BR-804), the sum against the unapplied amount, and every bill, in the
/// order of the bill ids (it exists, is posted, is owed by the same payer and by at least the amount). Then the
/// ledger, whose first posting takes the update lock on the payer's row of cached balances and keeps it to the
/// commit; payment and bills have the same payer, so that one row puts every allocation for the payer in a queue.
/// Then the commit writes the payment and the bills under their row versions: a cashier who read a payment or a
/// bill that somebody changed before this request got the lock gets 409 <c>concurrency_conflict</c>, and nothing is
/// written; the ledger may notice the same thing first, under its lock, and that is answered the same way. An
/// allocation takes no document number.
/// </para>
/// </summary>
public sealed class AllocatePaymentHandler(
    IValidator<AllocatePaymentCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    IBillSettlement bills,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<AllocatePaymentCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(AllocatePaymentCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var payment = await repository.GetAsync(command.Id, ct);
        if (payment is null)
        {
            return PaymentsErrors.PaymentNotFound();
        }

        if (RowVersionText.Of(payment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The validator refuses a line without an amount, so no request takes the other branch. The bills are taken
        // in the order of their ids, the same in every request, whatever order the client sent them in.
        var lines = command.Allocations
            .Select(line => (line.BillId, Amount: Money.Of(line.Amount ?? throw new ArgumentException("An allocation has no amount.", nameof(command)))))
            .OrderBy(line => line.BillId.Value)
            .ToArray();

        var affordable = payment.CheckCanAllocate(lines.Select(line => line.Amount).Sum());
        if (affordable.IsFailure)
        {
            return affordable.Error;
        }

        // One reading of the clock for the use case: the instant of the allocations, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var today = clock.ToIstDate(now);

        var made = new List<(PaymentAllocation Allocation, string BillNumber)>(lines.Length);
        foreach (var (billId, amount) in lines)
        {
            var settled = await bills.ApplyAsync(billId, payment.PayerId, amount, ct);
            if (settled.IsFailure)
            {
                return settled.Error;
            }

            var allocated = payment.Allocate(billId, amount, now, currentUser.UserId);
            if (allocated.IsFailure)
            {
                return allocated.Error;
            }

            // Adding the allocation gives it its id, which its ledger posting names as its document.
            repository.Add(allocated.Value);
            made.Add((allocated.Value, settled.Value.Number));
        }

        // From the first posting on, the payer's row of cached balances is locked until the commit.
        foreach (var (allocation, billNumber) in made)
        {
            var template = PostingTemplate.PaymentAllocated(allocation.Id, payment.Id, allocation.BillId, payment.PayerId, allocation.Amount, today, payment.Number, billNumber);
            var entered = await ledger.PostAsync(template, ct);
            if (entered.IsFailure)
            {
                // Both documents accepted the amount as they were read. A ledger that finds a balance going below
                // zero has seen what another request committed in between: the documents read here are stale, and
                // the client is told what it is told for any stale version (BR-804, ADR 0024).
                return LedgerRefusal.IsBalanceBelowZero(entered.Error) ? CommonErrors.ConcurrencyConflict() : entered.Error;
            }

            payment.AttachAllocationPosting(allocation, entered.Value);
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
