using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Money received from a payer (BR-301, BR-302, BR-309; docs/domain-model.md, section 4.6). A payment has no draft:
/// recording it posts it, so it is created with its gapless number, and its amount, date, method, reference and
/// payer never change afterwards (BR-006). What is not yet applied to a bill is <see cref="UnappliedAmount"/>, the
/// cache of the payment's unapplied cash in the ledger (BR-405); on a new payment that is the whole amount.
/// <para>
/// Whether the payer exists and is active is the use case's question (BR-309): it needs another aggregate. The date
/// follows BR-205, with today and the tenant's back-dating limit handed in as a <see cref="BusinessDateWindow"/>.
/// Allocations, reversals and refunds arrive with their slices.
/// </para>
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reference"/>.</summary>
    public const int ReferenceMaxLength = 100;

    /// <summary>Longest allowed <see cref="Notes"/>.</summary>
    public const int NotesMaxLength = 500;

    /// <summary>The largest amount one payment may have: sums of payments stay far inside what a <c>decimal(18,2)</c> column holds.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    // Entity Framework creates a payment with this constructor and then sets the properties.
    private Payment()
    {
        Number = string.Empty;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The gapless number of the payment, as it is printed on the receipt slip (BR-801).</summary>
    public string Number { get; private set; }

    /// <summary>The fiscal year of the payment date, which the number belongs to (BR-004).</summary>
    public FiscalYear FiscalYear { get; private set; }

    /// <summary>The position of the payment in the series of its fiscal year, from 1 (BR-801).</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>The payer the money came from. It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The business date the money was received, a calendar date in India (BR-003, BR-205).</summary>
    public DateOnly PaymentDate { get; private set; }

    /// <summary>How the money was received (BR-301).</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number; required unless the method is cash (BR-301).</summary>
    public string? Reference { get; private set; }

    /// <summary>The money received, above zero.</summary>
    public Money Amount { get; private set; }

    /// <summary>What of the amount is not yet applied to a bill or refunded (BR-405). The whole amount on a new payment.</summary>
    public Money UnappliedAmount { get; private set; }

    /// <summary>Where the payment is in its life.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>A note of the cashier.</summary>
    public string? Notes { get; private set; }

    /// <summary>The ledger posting that recorded the payment: Dr Cash, Cr UnappliedCash (BR-302). Set by the use case before it saves.</summary>
    public LedgerPostingId PostingId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>
    /// Whether a payment dated <paramref name="paymentDate"/> may be recorded today (BR-205). The use case asks before
    /// it takes the number, so that a refusal never touches the series (BR-801).
    /// </summary>
    public static Result CheckDate(DateOnly paymentDate, BusinessDateWindow window)
    {
        if (paymentDate > window.Latest)
        {
            return PaymentsErrors.PaymentDateInFuture();
        }

        return paymentDate < window.Earliest ? PaymentsErrors.PaymentDateTooOld(window.Earliest) : Result.Success();
    }

    /// <summary>
    /// Records, and thereby posts, a payment under <paramref name="number"/> (BR-302). The date must lie in
    /// <paramref name="window"/>; a date outside it is returned as a failure and nothing is created.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The payer's id is empty; the amount is not above zero or above <see cref="MaxAmount"/>; the method is not one
    /// the enum defines; a method other than cash has no reference; a text is too long; or the number is not a payment
    /// number of the fiscal year of the payment date. The validator of the use case refuses all of these first.
    /// </exception>
    public static Result<Payment> Record(
        PayerId payerId,
        DateOnly paymentDate,
        PaymentMethod method,
        string? reference,
        Money amount,
        string? notes,
        DocumentNumber number,
        BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("A payment comes from a payer; the empty id is not one.", nameof(payerId));
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "The payment method is not one the enum defines.");
        }

        if (!amount.IsPositive || amount.Amount > MaxAmount)
        {
            throw new ArgumentException($"A payment has an amount above zero and at most {MaxAmount}; the validator of the use case should have refused it.", nameof(amount));
        }

        var trimmedReference = Field.Optional(reference, ReferenceMaxLength);
        if (method != PaymentMethod.Cash && trimmedReference is null)
        {
            throw new ArgumentException("A payment that is not cash has a reference (BR-301); the validator of the use case should have refused it.", nameof(reference));
        }

        var trimmedNotes = Field.Optional(notes, NotesMaxLength);

        var dated = CheckDate(paymentDate, window);
        if (dated.IsFailure)
        {
            return dated.Error;
        }

        if (number.Type != DocumentType.Payment || number.FiscalYear != Common.FiscalYear.FromDate(paymentDate))
        {
            throw new ArgumentException("A payment is recorded under a payment number of the fiscal year of its payment date (BR-302).", nameof(number));
        }

        var payment = new Payment
        {
            Number = number.Value,
            FiscalYear = number.FiscalYear,
            SequenceNumber = number.Sequence,
            PayerId = payerId,
            PaymentDate = paymentDate,
            Method = method,
            Reference = trimmedReference,
            Amount = amount,
            UnappliedAmount = amount,
            Status = PaymentStatus.Posted,
            Notes = trimmedNotes,
        };

        // The id is still empty here: it is assigned when the payment is added to the unit of work.
        payment.AddDomainEvent(new PaymentRecorded(number.Value, payerId, method, amount, paymentDate));

        return payment;
    }

    /// <summary>
    /// Records which ledger posting belongs to the payment. The use case calls it once, between the ledger and the
    /// save: the posting needs the id of the payment, which exists only after the payment joined the unit of work.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="postingId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The payment has its posting already.</exception>
    public void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("A payment belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (PostingId.Value != Guid.Empty)
        {
            throw new InvalidOperationException("The payment has its ledger posting already; a posting is never replaced (BR-006).");
        }

        PostingId = postingId;
    }
}
