using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// One financial event in the receivables subledger (BR-401, BR-402, BR-406; docs/database.md, sections 3.7 and 5):
/// which document it belongs to, whose payer, on which business date, and two or more entries whose debits equal
/// their credits. A posting is created complete, in the transaction of its document, and is never changed or
/// deleted afterwards (<see cref="IAppendOnly"/>); a correction is a reversing posting that names the original.
/// <para>
/// The checks here are the invariants of the ledger itself: lines that break them are a bug in the code that built
/// them, so they throw. Whether a balance may fall (BR-405) needs the ledger so far and is checked where it is read.
/// </para>
/// </summary>
public sealed class LedgerPosting : Entity<LedgerPostingId>, ITenantScoped, IAppendOnly
{
    /// <summary>Longest allowed <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 300;

    private readonly List<LedgerEntry> _entries = [];

    // Entity Framework creates a posting with this constructor, sets the properties and fills the entries.
    private LedgerPosting()
    {
        Description = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>When the posting was written, in UTC (BR-406).</summary>
    public DateTimeOffset PostedAtUtc { get; private set; }

    /// <summary>The business date of the document, a calendar date in India (BR-406).</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>The financial event the posting records.</summary>
    public LedgerPostingKind Kind { get; private set; }

    /// <summary>The kind of document the posting belongs to.</summary>
    public LedgerDocumentType DocumentType { get; private set; }

    /// <summary>The id of that document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>The payer whose balances the posting moves.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The posting this one reverses; null unless it is a reversal.</summary>
    public LedgerPostingId? ReversalOfPostingId { get; private set; }

    /// <summary>What happened, for a person reading the ledger: <c>Bill BIL/26-27/000123 posted</c>.</summary>
    public string Description { get; private set; }

    /// <summary>Who caused the posting; null when no user did.</summary>
    public UserId? PostedByUserId { get; private set; }

    /// <summary>The correlation id of the request that caused the posting (BR-703).</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>
    /// The cashier's shift whose drawer the posting moved cash in or out of (BR-311, BR-312; ADR 0060); null for a
    /// posting that moved no cash of a shift. Its business date is the shift's.
    /// </summary>
    public CashierShiftId? ShiftId { get; private set; }

    /// <summary>The entries, in the order of their line numbers.</summary>
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>Creates a posting with its entries.</summary>
    /// <exception cref="ArgumentException">
    /// An id is empty; there are fewer than two lines; a line has no amount, an amount on both sides or a negative one;
    /// a subject account has no subject or the wrong kind, or a control account has one (BR-403); or the debits do not
    /// equal the credits (BR-401).
    /// </exception>
    public static LedgerPosting Create(
        LedgerPostingKind kind,
        LedgerDocumentType documentType,
        Guid documentId,
        PayerId payerId,
        DateOnly businessDate,
        string description,
        IReadOnlyList<LedgerLine> lines,
        DateTimeOffset postedAtUtc,
        UserId? postedBy,
        Guid correlationId,
        LedgerPostingId? reversalOf = null,
        CashierShiftId? shiftId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!Enum.IsDefined(kind) || !Enum.IsDefined(documentType))
        {
            throw new ArgumentException("The kind and the document type of a posting are ones the enums define.", nameof(kind));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A posting belongs to a document; the empty id is not one.", nameof(documentId));
        }

        if (payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("A posting moves the balances of a payer; the empty id is not one.", nameof(payerId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A posting needs the correlation id of its request (BR-703).", nameof(correlationId));
        }

        if (shiftId is { } shift && shift.Value == Guid.Empty)
        {
            throw new ArgumentException("A posting names the shift it belongs to; leave it null otherwise.", nameof(shiftId));
        }

        if (reversalOf is { } original && original.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal names the posting it reverses; leave it null otherwise.", nameof(reversalOf));
        }

        Check(lines);

        var posting = new LedgerPosting
        {
            Kind = kind,
            DocumentType = documentType,
            DocumentId = documentId,
            PayerId = payerId,
            BusinessDate = businessDate,
            Description = Field.Required(description, DescriptionMaxLength),
            PostedAtUtc = postedAtUtc,
            PostedByUserId = postedBy,
            CorrelationId = correlationId,
            ReversalOfPostingId = reversalOf,
            ShiftId = shiftId,
        };

        for (var index = 0; index < lines.Count; index++)
        {
            posting._entries.Add(LedgerEntry.Create((short)(index + 1), lines[index], payerId));
        }

        return posting;
    }

    /// <summary>
    /// What the posting does to each subject it names: the change of that bill's receivable, that payment's unapplied
    /// cash or that credit note's available credit, positive when the balance grows.
    /// </summary>
    public IReadOnlyList<(LedgerAccount Account, LedgerSubject Subject, Money Change)> SubjectChanges()
        => [.. _entries
            .Where(entry => entry.Subject() is not null)
            .GroupBy(entry => (entry.Account, Subject: entry.Subject()!.Value))
            .Select(group => (group.Key.Account, group.Key.Subject, group.Select(entry => entry.BalanceChange()).Sum()))];

    /// <summary>What the posting does to the three balances of its payer, each positive when the balance grows (BR-404).</summary>
    public PayerBalanceChange PayerChange() => new(
        ChangeOf(LedgerAccount.Receivable),
        ChangeOf(LedgerAccount.UnappliedCash),
        ChangeOf(LedgerAccount.CreditAvailable));

    private Money ChangeOf(LedgerAccount account)
        => _entries.Where(entry => entry.Account == account).Select(entry => entry.BalanceChange()).Sum();

    private static void Check(IReadOnlyList<LedgerLine> lines)
    {
        if (lines.Count < 2)
        {
            throw new ArgumentException("A posting has two or more lines (BR-401).", nameof(lines));
        }

        if (lines.Count > short.MaxValue)
        {
            throw new ArgumentException("A posting has more lines than a line number can count.", nameof(lines));
        }

        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);

            if (!Enum.IsDefined(line.Account))
            {
                throw new ArgumentException("The account of a line is one the enum defines.", nameof(lines));
            }

            var oneSide = (line.Debit.IsPositive && line.Credit.IsZero) || (line.Debit.IsZero && line.Credit.IsPositive);
            if (!oneSide)
            {
                throw new ArgumentException("A line has an amount above zero on exactly one side.", nameof(lines));
            }

            if (SubjectTypeOf(line.Account) != line.Subject?.Type || line.Subject is { Id: var id } && id == Guid.Empty)
            {
                throw new ArgumentException(
                    $"A line on {line.Account} names {SubjectTypeOf(line.Account)?.ToString() ?? "no subject"} as its subject (BR-403).",
                    nameof(lines));
            }
        }

        var debits = lines.Select(line => line.Debit).Sum();
        var credits = lines.Select(line => line.Credit).Sum();
        if (debits != credits)
        {
            throw new ArgumentException($"The debits of a posting equal its credits (BR-401); these are {debits} and {credits}.", nameof(lines));
        }
    }

    /// <summary>The kind of subject an account is kept for, or null for a control account (BR-403).</summary>
    public static LedgerSubjectType? SubjectTypeOf(LedgerAccount account) => account switch
    {
        LedgerAccount.Receivable => LedgerSubjectType.Bill,
        LedgerAccount.UnappliedCash => LedgerSubjectType.Payment,
        LedgerAccount.CreditAvailable => LedgerSubjectType.CreditNote,
        _ => null,
    };
}
