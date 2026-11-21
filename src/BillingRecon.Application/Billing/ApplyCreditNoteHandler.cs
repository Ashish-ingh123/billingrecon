using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Applies available credit of a posted credit note to posted bills of its payer (BR-213), built like the allocation
/// of a payment (ADR 0024): for every line of the request the credit note's available amount and the bill's balance
/// due fall by the same amount, the ledger gets Dr CreditAvailable(credit note) / Cr Receivable(bill), and one commit
/// saves all of it, or none. No second person is asked: BR-604 does not list this action.
/// <para>
/// Order and lock: the shape, the credit note and the version the client read, the sum against the available amount,
/// then every bill in the order of the bill ids, through <see cref="IBillSettlement"/>; then the ledger, whose first
/// posting locks the payer's row of cached balances; then the save, under the row versions of the credit note and of
/// every bill. A balance the ledger finds going below zero is answered as a stale version, 409.
/// </para>
/// </summary>
public sealed class ApplyCreditNoteHandler(
    IValidator<ApplyCreditNoteCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICreditNoteRepository repository,
    IBillSettlement bills,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApplyCreditNoteCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(ApplyCreditNoteCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var creditNote = await repository.GetAsync(command.Id, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (RowVersionText.Of(creditNote) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The validator refuses a line without an amount, so no request takes the other branch. The bills are taken
        // in the order of their ids, the same in every request.
        var lines = command.Applications
            .Select(line => (line.BillId, Amount: Money.Of(line.Amount ?? throw new ArgumentException("An application has no amount.", nameof(command)))))
            .OrderBy(line => line.BillId.Value)
            .ToArray();

        var affordable = creditNote.CheckCanApply(lines.Select(line => line.Amount).Sum());
        if (affordable.IsFailure)
        {
            return affordable.Error;
        }

        var now = clock.UtcNow;
        var today = clock.ToIstDate(now);

        var made = new List<(CreditNoteAllocation Allocation, string BillNumber)>(lines.Length);
        foreach (var (billId, amount) in lines)
        {
            var settled = await bills.ApplyAsync(billId, creditNote.PayerId, amount, ct);
            if (settled.IsFailure)
            {
                return settled.Error;
            }

            var applied = creditNote.Apply(billId, amount, now, currentUser.UserId);
            if (applied.IsFailure)
            {
                return applied.Error;
            }

            repository.Add(applied.Value);
            made.Add((applied.Value, settled.Value.Number));
        }

        // From the first posting on, the payer's row of cached balances is locked until the commit.
        foreach (var (allocation, billNumber) in made)
        {
            // CheckCanApply refused a credit note without its number.
            var template = PostingTemplate.CreditApplied(allocation.Id, creditNote.Id, allocation.BillId, creditNote.PayerId, allocation.Amount, today, creditNote.Number ?? string.Empty, billNumber);
            var entered = await ledger.PostAsync(template, ct);
            if (entered.IsFailure)
            {
                return LedgerRefusal.IsBalanceBelowZero(entered.Error) ? CommonErrors.ConcurrencyConflict() : entered.Error;
            }

            creditNote.AttachApplicationPosting(allocation, entered.Value);
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
