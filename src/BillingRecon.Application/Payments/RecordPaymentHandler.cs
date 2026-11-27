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
/// <para>
/// A cash payment needs the open shift of the user who records it (BR-311; ADR 0060): <c>shift_not_open</c> without
/// one, <c>shift_date_mismatch</c> when it is dated another day than the shift. Its ledger posting names the shift.
/// </para>
/// </summary>
public sealed class RecordPaymentHandler(
    IValidator<RecordPaymentCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    ICashierShiftRepository shifts,
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
        // Cash is taken in the open shift of whoever records it, and is dated the business date of that shift, which is
        // also the date of a cash payment sent without one (BR-311; ADR 0060).
        CashierShift? shift = null;
        if (method == PaymentMethod.Cash)
        {
            if (currentUser.UserId is not { } cashier || await shifts.FindOpenOfAsync(cashier, ct) is not { } open)
            {
                return PaymentsErrors.ShiftNotOpen();
            }

            shift = open;
        }

        var paymentDate = command.PaymentDate ?? shift?.BusinessDate ?? today;

        var dated = Payment.CheckDate(paymentDate, window);
        if (dated.IsFailure)
        {
            return dated.Error;
        }

        if (shift?.CheckTakes(paymentDate) is { IsFailure: true } otherDay)
        {
            return otherDay.Error;
        }

        var payer = await payers.EnsureActiveAsync(command.PayerId, ct);
        if (payer.IsFailure)
        {
            return payer.Error;
        }

        // The same receipt is not recorded twice (BR-301 as amended 2026-11-26; review finding R2): a request whose
        // answer was lost and that is typed again carries a new key, and only its reference tells it from new money.
        // Cash has no reference to compare, and two equal cash payments in a row are ordinary.
        var reference = command.Reference?.Trim();
        if (method != PaymentMethod.Cash && !string.IsNullOrEmpty(reference)
            && await repository.FindNumberByReferenceAsync(command.PayerId, method, reference, ct) is { Length: > 0 } recordedAs)
        {
            return PaymentsErrors.PaymentReferenceAlreadyRecorded(recordedAs);
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

        // The posting names the shift, and the shift counts the document: its row version changes, so a closing that
        // read the ledger without this payment cannot commit (ADR 0060).
        shift?.TakeCashDocument();
        var template = PostingTemplate.PaymentRecorded(payment.Id, payment.PayerId, payment.Amount, payment.PaymentDate, number);
        var entered = await ledger.PostAsync(template with { ShiftId = shift?.Id }, ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        payment.AttachPosting(entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        if (committed.IsFailure)
        {
            // Two requests with one reference at the same instant: the unique index let one through, and this is the other.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode && method != PaymentMethod.Cash
                ? PaymentsErrors.PaymentReferenceAlreadyRecorded(null)
                : committed.Error;
        }

        return payment.ToDto();
    }
}
