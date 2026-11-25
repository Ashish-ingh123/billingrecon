using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// The opening balances of a hospital, brought in once from the system before (ADR 0053): the unpaid bills and the
/// advances as at the close of the day before <see cref="CutOverDate"/>, as three CSV files. One person submits the
/// batch and a second approves it (BR-603); the approval is the posting of every opening receivable and advance, in one
/// transaction. The files are kept as they were submitted, so what is approved is what was checked, and their hash makes
/// a batch that was brought in once impossible to bring in twice.
/// </summary>
public sealed class OpeningBatch : AggregateRoot<OpeningBatchId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>The length of <see cref="ContentHash"/>: a SHA-256 in hexadecimal.</summary>
    public const int ContentHashLength = 64;

    /// <summary>How many opening receivables and advances one batch may hold together: one transaction posts them all.</summary>
    public const int MaxDocuments = 2000;

    // Entity Framework creates a batch with this constructor and then sets the properties.
    private OpeningBatch()
    {
        ContentHash = string.Empty;
        BillsCsv = string.Empty;
        AdvancesCsv = string.Empty;
        TrialBalanceCsv = string.Empty;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The first day on which documents are raised here; the opening balances are as at the day before.</summary>
    public DateOnly CutOverDate { get; private set; }

    /// <summary>
    /// The SHA-256 of the cut-over date and the three files, in upper-case hexadecimal; unique in the tenant among the
    /// batches that were not rejected.
    /// </summary>
    public string ContentHash { get; private set; }

    /// <summary>The unpaid bills of the system before, as submitted. Kept out of every audit image.</summary>
    public string BillsCsv { get; private set; }

    /// <summary>The advances and credit balances of the system before, as submitted. Kept out of every audit image.</summary>
    public string AdvancesCsv { get; private set; }

    /// <summary>The trial balance of the system before, as submitted. Kept out of every audit image.</summary>
    public string TrialBalanceCsv { get; private set; }

    /// <summary>How many opening receivables the batch holds.</summary>
    public int Receivables { get; private set; }

    /// <summary>The sum of the opening receivables.</summary>
    public Money ReceivableTotal { get; private set; }

    /// <summary>How many opening advances the batch holds.</summary>
    public int Advances { get; private set; }

    /// <summary>The sum of the opening advances.</summary>
    public Money AdvanceTotal { get; private set; }

    /// <summary>Where the batch stands.</summary>
    public OpeningBatchStatus Status { get; private set; }

    /// <summary>When the batch was approved or rejected; null while it waits.</summary>
    public DateTimeOffset? DecidedAtUtc { get; private set; }

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
    /// A batch that waits for approval, holding the files as they were checked. The counts and totals are those the check
    /// of the files found; that no row was rejected, and that the totals equal the trial balance, is for the use case to
    /// establish before it comes here.
    /// </summary>
    /// <exception cref="ArgumentException">The hash is not a SHA-256, a count is negative, the batch is empty or too large.</exception>
    public static OpeningBatch Submit(
        DateOnly cutOverDate,
        string contentHash,
        string billsCsv,
        string advancesCsv,
        string trialBalanceCsv,
        int receivables,
        Money receivableTotal,
        int advances,
        Money advanceTotal)
    {
        ArgumentNullException.ThrowIfNull(billsCsv);
        ArgumentNullException.ThrowIfNull(advancesCsv);
        ArgumentNullException.ThrowIfNull(trialBalanceCsv);

        if (contentHash is not { Length: ContentHashLength } || !contentHash.All(char.IsAsciiHexDigitUpper))
        {
            throw new ArgumentException("The hash of a batch is a SHA-256 in upper-case hexadecimal.", nameof(contentHash));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(receivables);
        ArgumentOutOfRangeException.ThrowIfNegative(advances);
        if (receivables + advances is 0 or > MaxDocuments)
        {
            throw new ArgumentException($"A batch brings in 1 to {MaxDocuments} opening balances.", nameof(receivables));
        }

        if (receivableTotal.IsNegative || advanceTotal.IsNegative)
        {
            throw new ArgumentException("The totals of a batch are not below zero.", nameof(receivableTotal));
        }

        var batch = new OpeningBatch
        {
            CutOverDate = cutOverDate,
            ContentHash = contentHash,
            BillsCsv = billsCsv,
            AdvancesCsv = advancesCsv,
            TrialBalanceCsv = trialBalanceCsv,
            Receivables = receivables,
            ReceivableTotal = receivableTotal,
            Advances = advances,
            AdvanceTotal = advanceTotal,
            Status = OpeningBatchStatus.PendingApproval,
        };

        // The id is still empty here: it is assigned when the batch is added to the unit of work.
        batch.AddDomainEvent(new OpeningBatchSubmitted(cutOverDate, receivables, receivableTotal, advances, advanceTotal));

        return batch;
    }

    /// <summary>Success while the batch waits for its decision; otherwise it was decided already.</summary>
    public Result CheckIsPending()
        => Status == OpeningBatchStatus.PendingApproval ? Result.Success() : ReceivablesErrors.OpeningBatchNotPending();

    /// <summary>
    /// The batch was approved at <paramref name="decidedAtUtc"/>: the use case posts its opening balances in the same
    /// transaction.
    /// </summary>
    public Result RecordApproval(DateTimeOffset decidedAtUtc)
    {
        var pending = CheckIsPending();
        if (pending.IsFailure)
        {
            return pending;
        }

        Status = OpeningBatchStatus.Approved;
        DecidedAtUtc = decidedAtUtc;
        AddDomainEvent(new OpeningBatchApproved(Id, Receivables, ReceivableTotal, Advances, AdvanceTotal));

        return Result.Success();
    }

    /// <summary>
    /// The batch was rejected at <paramref name="decidedAtUtc"/> for <paramref name="reason"/>: nothing of it is posted.
    /// </summary>
    /// <exception cref="ArgumentException">The reason is blank or too long.</exception>
    public Result RecordRejection(string reason, DateTimeOffset decidedAtUtc)
    {
        var why = Field.Required(reason, ApprovalRequest.CommentMaxLength);

        var pending = CheckIsPending();
        if (pending.IsFailure)
        {
            return pending;
        }

        Status = OpeningBatchStatus.Rejected;
        DecidedAtUtc = decidedAtUtc;
        AddDomainEvent(new OpeningBatchRejected(Id, why));

        return Result.Success();
    }
}
