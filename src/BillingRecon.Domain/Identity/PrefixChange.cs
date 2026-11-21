using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// A TenantAdmin's request for a new prefix of one document type, which a second person decides (BR-802, BR-604;
/// ADR 0033). It records the prefix the type had when it was asked for and the one it is to get; its approval request
/// records who decided and why. Approved, the use case gives the type the new prefix on the tenant and on the series of
/// the current fiscal year; rejected, nothing changes. Decided once. At most one change of a type waits at a time, a
/// filtered unique index keeps it so.
/// </summary>
public sealed class PrefixChange : AggregateRoot<PrefixChangeId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    // Entity Framework creates a prefix change with this constructor and then sets the properties.
    private PrefixChange()
    {
        FromPrefix = string.Empty;
        ToPrefix = string.Empty;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The document type whose prefix changes.</summary>
    public DocumentType DocumentType { get; private set; }

    /// <summary>The prefix the type had when the change was asked for.</summary>
    public string FromPrefix { get; private set; }

    /// <summary>The prefix the type gets when the change is approved.</summary>
    public string ToPrefix { get; private set; }

    /// <summary>Where the change stands.</summary>
    public PrefixChangeStatus Status { get; private set; }

    /// <summary>When a checker decided it; null while it waits.</summary>
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>When it was asked for.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Who asked for it: the maker.</summary>
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>A change of <paramref name="documentType"/> from <paramref name="fromPrefix"/> to <paramref name="toPrefix"/>, waiting.</summary>
    /// <exception cref="ArgumentException">
    /// The type is not one the enum defines, a prefix is not 1 to 3 characters <c>[A-Z0-9]</c>, or the two are equal:
    /// bugs in the caller, whose use case refuses those cases first.
    /// </exception>
    public static PrefixChange Request(DocumentType documentType, string fromPrefix, string toPrefix)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new ArgumentException("The document type is not one the enum defines.", nameof(documentType));
        }

        if (!DocumentNumber.IsPrefix(fromPrefix) || !DocumentNumber.IsPrefix(toPrefix))
        {
            throw new ArgumentException($"A prefix has 1 to {DocumentNumber.PrefixMaxLength} characters, each a capital letter or a digit (BR-802).", nameof(toPrefix));
        }

        if (fromPrefix == toPrefix)
        {
            throw new ArgumentException("A prefix change changes the prefix.", nameof(toPrefix));
        }

        var change = new PrefixChange
        {
            DocumentType = documentType,
            FromPrefix = fromPrefix,
            ToPrefix = toPrefix,
            Status = PrefixChangeStatus.Pending,
        };

        // The id is still empty here: it is assigned when the change is added to the unit of work.
        change.AddDomainEvent(new PrefixChangeRequested(documentType, fromPrefix, toPrefix));

        return change;
    }

    /// <summary>Succeeds while the change waits; <c>prefix_change_not_pending</c> once it is decided.</summary>
    public Result CheckIsPending() => Status == PrefixChangeStatus.Pending ? Result.Success() : IdentityErrors.PrefixChangeNotPending();

    /// <summary>The checker approved: the change is made by the use case in the same commit (BR-606).</summary>
    public Result RecordApproval(DateTimeOffset decidedAtUtc)
    {
        var pending = CheckIsPending();
        if (pending.IsFailure)
        {
            return pending;
        }

        Status = PrefixChangeStatus.Approved;
        DecidedAtUtc = decidedAtUtc;
        AddDomainEvent(new PrefixChangeApproved(Id, DocumentType, FromPrefix, ToPrefix));

        return Result.Success();
    }

    /// <summary>The checker rejected it, saying why (BR-605); nothing changes.</summary>
    /// <exception cref="ArgumentException">The reason is blank or longer than an approval request's comment.</exception>
    public Result RecordRejection(string reason, DateTimeOffset decidedAtUtc)
    {
        var why = Field.Required(reason, ApprovalRequest.CommentMaxLength);

        var pending = CheckIsPending();
        if (pending.IsFailure)
        {
            return pending;
        }

        Status = PrefixChangeStatus.Rejected;
        DecidedAtUtc = decidedAtUtc;
        AddDomainEvent(new PrefixChangeRejected(Id, why));

        return Result.Success();
    }
}
