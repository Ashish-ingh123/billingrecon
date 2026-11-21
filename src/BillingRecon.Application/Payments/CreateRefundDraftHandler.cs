using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Drafts a refund (US-34; BR-307, BR-309): the source must exist and still be usable, a payment that is posted or a
/// posted credit note, and its payer active; the refund takes that payer. The date follows BR-205 and is not before
/// the source's date. Nothing financial happens: how much the source has left is asked when the maker submits.
/// </summary>
public sealed class CreateRefundDraftHandler(
    IValidator<CreateRefundDraftCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IPaymentRepository payments,
    ICreditNoteSettlement creditNotes,
    IActivePayers payers,
    IRefundRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateRefundDraftCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(CreateRefundDraftCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without these, so no request takes these branches.
        var amount = Money.Of(command.Amount ?? throw new ArgumentException("The command has no amount.", nameof(command)));
        var method = command.Method ?? throw new ArgumentException("The command has no method.", nameof(command));
        var source = command.PaymentId is { } paymentId
            ? RefundSource.FromPayment(paymentId)
            : RefundSource.FromCreditNote(command.CreditNoteId ?? throw new ArgumentException("The command has no source.", nameof(command)));

        var found = await RefundSources.FindAsync(source, payments, creditNotes, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var facts = found.Value;
        var active = await payers.EnsureActiveAsync(facts.PayerId, ct);
        if (active.IsFailure)
        {
            return active.Error;
        }

        // One reading of the clock for the use case (BR-003).
        var today = clock.TodayIst;
        var dates = await settings.GetDocumentSettingsAsync(ct);

        var created = Refund.Create(
            source,
            facts.PayerId,
            facts.Date,
            command.RefundDate ?? today,
            amount,
            method,
            command.Reference,
            command.Reason,
            new BusinessDateWindow(today, dates.MaxBackdateDays));
        if (created.IsFailure)
        {
            return created.Error;
        }

        var refund = created.Value;
        repository.Add(refund);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }
}
