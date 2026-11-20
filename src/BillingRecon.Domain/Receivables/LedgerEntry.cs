using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// One line of a ledger posting (BR-401 to BR-403): an account and an amount on exactly one side. An entry on a
/// subject account names its subject and its payer; an entry on a control account names neither. Entries exist only
/// inside their posting, are created by it, and are never changed or deleted (BR-402, <see cref="IAppendOnly"/>).
/// The id is a number the database assigns, like the id of an audit record.
/// </summary>
public sealed class LedgerEntry : ITenantScoped, IAppendOnly
{
    // Entity Framework creates an entry with this constructor and then sets the properties.
    private LedgerEntry()
    {
    }

    /// <summary>The number of the entry, assigned by the database when it is written. Zero until then.</summary>
    public long Id { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The posting the entry belongs to. Set when the posting is saved.</summary>
    public LedgerPostingId PostingId { get; private set; }

    /// <summary>The position of the entry in its posting, from 1.</summary>
    public short LineNo { get; private set; }

    /// <summary>The account.</summary>
    public LedgerAccount Account { get; private set; }

    /// <summary>The kind of document a subject account is kept for; null on a control account.</summary>
    public LedgerSubjectType? SubjectType { get; private set; }

    /// <summary>The id of that document; null on a control account.</summary>
    public Guid? SubjectId { get; private set; }

    /// <summary>The payer of a subject account; null on a control account.</summary>
    public PayerId? PayerId { get; private set; }

    /// <summary>The debit amount; zero on a credit entry.</summary>
    public Money Debit { get; private set; }

    /// <summary>The credit amount; zero on a debit entry.</summary>
    public Money Credit { get; private set; }

    /// <summary>The subject of the entry, or null on a control account.</summary>
    public LedgerSubject? Subject()
        => SubjectType is { } type && SubjectId is { } id ? new LedgerSubject(type, id) : null;

    /// <summary>
    /// What the entry does to the balance of its account, in the direction that account grows (docs/database.md,
    /// section 5.2): a debit raises Receivable, Cash and Adjustments, a credit raises the other three.
    /// </summary>
    public Money BalanceChange()
        => Account is LedgerAccount.Receivable or LedgerAccount.Cash or LedgerAccount.Adjustments ? Debit - Credit : Credit - Debit;

    /// <summary>The entry at position <paramref name="lineNo"/> for a line the posting has checked.</summary>
    internal static LedgerEntry Create(short lineNo, LedgerLine line, PayerId payerId) => new()
    {
        LineNo = lineNo,
        Account = line.Account,
        SubjectType = line.Subject?.Type,
        SubjectId = line.Subject?.Id,
        PayerId = line.Subject is null ? null : payerId,
        Debit = line.Debit,
        Credit = line.Credit,
    };
}
