using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Common;

/// <summary>
/// A maker's request that a second person approve an action on a document (BR-603 to BR-606; docs/domain-model.md,
/// sections 4.9 and 5.4): posting a bill at or above the tenant's threshold today, credit notes, adjustments,
/// refunds and reversals later. It records who asked, who decided, when and with which comment, and nothing
/// financial: the document carries that. A request is decided once, by a user other than the maker; the checker's
/// approval is the action itself, carried out in the same transaction by the use case of the document (BR-606).
/// <para>
/// It lives in Common because every business module uses it, and it names its document by kind and id, never by
/// type, so Common depends on no module (ADR 0003). At most one request of a document is pending; a filtered unique
/// index enforces it (BR-605).
/// </para>
/// </summary>
public sealed class ApprovalRequest : AggregateRoot<ApprovalRequestId>, ITenantScoped, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="MakerComment"/> and <see cref="CheckerComment"/>.</summary>
    public const int CommentMaxLength = 500;

    /// <summary>The roles of which a checker holds at least one (BR-603).</summary>
    public static readonly IReadOnlyList<Role> CheckerRoles = [Role.Supervisor, Role.TenantAdmin];

    // Entity Framework creates a request with this constructor and then sets the properties.
    private ApprovalRequest()
    {
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The kind of document the request is about.</summary>
    public ApprovalDocumentType DocumentType { get; private set; }

    /// <summary>The id of that document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>What the maker asks to be done.</summary>
    public ApprovalAction Action { get; private set; }

    /// <summary>Who asked.</summary>
    public UserId MakerUserId { get; private set; }

    /// <summary>What the maker said about it, if anything.</summary>
    public string? MakerComment { get; private set; }

    /// <summary>Who decided; null while the request is pending. Never the maker (BR-603).</summary>
    public UserId? CheckerUserId { get; private set; }

    /// <summary>What the checker said: required for a rejection (BR-605), optional for an approval.</summary>
    public string? CheckerComment { get; private set; }

    /// <summary>Where the request stands.</summary>
    public ApprovalStatus Status { get; private set; }

    /// <summary>When the maker asked.</summary>
    public DateTimeOffset RequestedAtUtc { get; private set; }

    /// <summary>When the checker decided; null while the request is pending.</summary>
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>The correlation id of the request that opened it (BR-703).</summary>
    public Guid CorrelationId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>Opens a pending request of <paramref name="maker"/> for an action on a document.</summary>
    /// <exception cref="ArgumentException">An id is empty, an enum value is not defined, or the comment is too long.</exception>
    public static ApprovalRequest Open(
        ApprovalDocumentType documentType,
        Guid documentId,
        ApprovalAction action,
        UserId maker,
        string? makerComment,
        DateTimeOffset requestedAtUtc,
        Guid correlationId)
    {
        if (!Enum.IsDefined(documentType) || !Enum.IsDefined(action))
        {
            throw new ArgumentException("The document type and the action of a request are ones the enums define.", nameof(documentType));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A request is about a document; the empty id is not one.", nameof(documentId));
        }

        if (maker.Value == Guid.Empty)
        {
            throw new ArgumentException("A request has a maker; the empty id is not a user.", nameof(maker));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A request needs the correlation id of the request that opened it (BR-703).", nameof(correlationId));
        }

        var request = new ApprovalRequest
        {
            DocumentType = documentType,
            DocumentId = documentId,
            Action = action,
            MakerUserId = maker,
            MakerComment = Field.Optional(makerComment, CommentMaxLength),
            Status = ApprovalStatus.Pending,
            RequestedAtUtc = requestedAtUtc,
            CorrelationId = correlationId,
        };

        // The id is still empty here: it is assigned when the request is added to the unit of work.
        request.AddDomainEvent(new ApprovalRequestOpened(documentType, documentId, action, maker));

        return request;
    }

    /// <summary>
    /// The checker approves (BR-606). Fails, and changes nothing, when the request is already decided
    /// (<c>approval_already_decided</c>), the checker is the maker (<c>approval_checker_is_maker</c>, BR-603), or the
    /// checker holds neither Supervisor nor TenantAdmin among <paramref name="checkerRoles"/>
    /// (<c>approval_checker_role</c>, BR-603, ADR 0032).
    /// </summary>
    /// <exception cref="ArgumentException">The checker's id is empty, or the comment is too long.</exception>
    public Result Approve(UserId checker, IReadOnlyCollection<Role> checkerRoles, string? comment, DateTimeOffset decidedAtUtc)
    {
        var note = Field.Optional(comment, CommentMaxLength);

        if (CheckDecision(checker, checkerRoles) is { } refused)
        {
            return refused;
        }

        Decide(ApprovalStatus.Approved, checker, note, decidedAtUtc);
        AddDomainEvent(new ApprovalRequestApproved(Id, DocumentType, DocumentId, checker));

        return Result.Success();
    }

    /// <summary>
    /// The checker rejects, saying why (BR-605). Fails, and changes nothing, when the request is already decided, the
    /// checker is the maker, or the checker holds neither Supervisor nor TenantAdmin (BR-603).
    /// </summary>
    /// <exception cref="ArgumentException">The checker's id is empty, or the comment is blank or too long.</exception>
    public Result Reject(UserId checker, IReadOnlyCollection<Role> checkerRoles, string comment, DateTimeOffset decidedAtUtc)
    {
        var why = Field.Required(comment, CommentMaxLength);

        if (CheckDecision(checker, checkerRoles) is { } refused)
        {
            return refused;
        }

        Decide(ApprovalStatus.Rejected, checker, why, decidedAtUtc);
        AddDomainEvent(new ApprovalRequestRejected(Id, DocumentType, DocumentId, checker, why));

        return Result.Success();
    }

    private Error? CheckDecision(UserId checker, IReadOnlyCollection<Role> checkerRoles)
    {
        ArgumentNullException.ThrowIfNull(checkerRoles);

        if (checker.Value == Guid.Empty)
        {
            throw new ArgumentException("A decision has a checker; the empty id is not a user.", nameof(checker));
        }

        if (Status != ApprovalStatus.Pending)
        {
            return CommonErrors.ApprovalAlreadyDecided();
        }

        if (checker == MakerUserId)
        {
            return CommonErrors.ApprovalCheckerIsMaker();
        }

        return checkerRoles.Any(CheckerRoles.Contains) ? null : CommonErrors.ApprovalCheckerRole();
    }

    private void Decide(ApprovalStatus status, UserId checker, string? comment, DateTimeOffset decidedAtUtc)
    {
        Status = status;
        CheckerUserId = checker;
        CheckerComment = comment;
        DecidedAtUtc = decidedAtUtc;
    }
}
