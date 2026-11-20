using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Records a payment, which posts it (US-30; BR-301, BR-302, BR-309): the gapless payment number of the fiscal year
/// of the payment date, Dr Cash / Cr UnappliedCash(payment), the payer's unapplied cash, the payment itself and its
/// audit record are one transaction (BR-007).
/// <para>
/// The order is that of every posting (ADR 0022). First everything that can refuse, with nothing locked: the shape
/// of the request, the date against today (BR-205), the payer (it exists and is active, BR-309). Then the number;
/// then the payment joins the unit of work, which gives it the id its ledger entry names; then the ledger, whose
/// posting the payment keeps; then one commit.
/// </para>
/// </summary>
public sealed class RecordPaymentHandler(
    IValidator<RecordPaymentCommand> validator,
    IClock clock,
    IPaymentRepository repository,
    ITenantSettingsReader tenantSettings,
    IActivePayers payers,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordPaymentCommand, PaymentDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentDto>> HandleAsync(RecordPaymentCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without a method or an amount, so no request takes these branches.
        var method = command.Method ?? throw new ArgumentException("The command has no payment method.", nameof(command));
        var amount = Money.Of(command.Amount ?? throw new ArgumentException("The command has no amount.", nameof(command)));

        // One reading of the clock for the use case: today in India from it (BR-003).
        var today = clock.ToIstDate(clock.UtcNow);
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(today, settings.MaxBackdateDays);
        var paymentDate = command.PaymentDate ?? today;

        var dated = Payment.CheckDate(paymentDate, window);
        if (dated.IsFailure)
        {
            return dated.Error;
        }

        var payer = await payers.EnsureActiveAsync(command.PayerId, ct);
        if (payer.IsFailure)
        {
            return payer.Error;
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.Payment, FiscalYear.FromDate(paymentDate), ct);

        var recorded = Payment.Record(command.PayerId, paymentDate, method, command.Reference, amount, command.Notes, number, window);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        // Adding the payment gives it its id, which the ledger entry names as its subject.
        var payment = recorded.Value;
        repository.Add(payment);

        var entered = await ledger.PostAsync(PostingTemplate.PaymentRecorded(payment.Id, payment.PayerId, payment.Amount, payment.PaymentDate, number), ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        payment.AttachPosting(entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDto() : committed.Error;
    }
}
