using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// One hospital or clinic: the unit of isolation and the holder of the tenant-wide settings (BR-607). The row is
/// not tenant-scoped; it is the tenant (ADR 0009).
/// </summary>
public sealed class Tenant : AggregateRoot<TenantId>, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Code"/>.</summary>
    public const int CodeMaxLength = 20;

    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Longest allowed document-number prefix (ADR 0008).</summary>
    public const int PrefixMaxLength = 3;

    /// <summary>Upper limit of <see cref="MaxBackdateDays"/> and <see cref="DefaultDueDays"/> (BR-205).</summary>
    public const int MaxDays = 365;

    /// <summary>How many days a new tenant allows a document to be back-dated (BR-205).</summary>
    public const int InitialMaxBackdateDays = 7;

    // Also the constructor Entity Framework uses to load a tenant: it passes the three stored values and then sets
    // the remaining properties, replacing the initial settings assigned here.
    private Tenant(string code, string name, DateTimeOffset createdAtUtc)
    {
        Code = code;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        Status = TenantStatus.Active;
        MaxBackdateDays = InitialMaxBackdateDays;
        DefaultDueDays = 0;
        BillPrefix = "BIL";
        CreditNotePrefix = "CN";
        PaymentPrefix = "PAY";
        AdjustmentPrefix = "ADJ";
        RefundPrefix = "REF";
        RowVersion = [];
    }

    /// <summary>Short, stable name used in logs and URLs: lower-case letters, digits and single hyphens. Unique.</summary>
    public string Code { get; private set; }

    /// <summary>Display name of the hospital or clinic.</summary>
    public string Name { get; private set; }

    /// <summary>Whether the tenant may use the system.</summary>
    public TenantStatus Status { get; private set; }

    /// <summary>
    /// A bill whose total reaches this amount needs a checker's approval before it is posted; null means bills never
    /// need approval (BR-207). Changing it is a TenantAdmin's audited action (BR-607, <see cref="ChangeSettings"/>).
    /// </summary>
    public Money? PostingApprovalThreshold { get; private set; }

    /// <summary>How many days before today a business date may lie (BR-205).</summary>
    public int MaxBackdateDays { get; private set; }

    /// <summary>Days between the bill date and the default due date (BR-205).</summary>
    public int DefaultDueDays { get; private set; }

    /// <summary>Number prefix of bills when a number series is first used (BR-802).</summary>
    public string BillPrefix { get; private set; }

    /// <summary>Number prefix of credit notes when a number series is first used (BR-802).</summary>
    public string CreditNotePrefix { get; private set; }

    /// <summary>Number prefix of payments when a number series is first used (BR-802).</summary>
    public string PaymentPrefix { get; private set; }

    /// <summary>Number prefix of adjustments when a number series is first used (BR-802).</summary>
    public string AdjustmentPrefix { get; private set; }

    /// <summary>Number prefix of refunds when a number series is first used (BR-802).</summary>
    public string RefundPrefix { get; private set; }

    /// <summary>The instant the tenant was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>The default prefix of every document type, in the order of <see cref="DocumentType"/>.</summary>
    public IReadOnlyDictionary<DocumentType, string> Prefixes => new Dictionary<DocumentType, string>
    {
        [DocumentType.Bill] = BillPrefix,
        [DocumentType.CreditNote] = CreditNotePrefix,
        [DocumentType.Payment] = PaymentPrefix,
        [DocumentType.Adjustment] = AdjustmentPrefix,
        [DocumentType.Refund] = RefundPrefix,
    };

    /// <summary>The default prefix of <paramref name="documentType"/>: the prefix of its next new series (BR-802).</summary>
    public string PrefixOf(DocumentType documentType) => documentType switch
    {
        DocumentType.Bill => BillPrefix,
        DocumentType.CreditNote => CreditNotePrefix,
        DocumentType.Payment => PaymentPrefix,
        DocumentType.Adjustment => AdjustmentPrefix,
        DocumentType.Refund => RefundPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Not a document type that takes a number."),
    };

    /// <summary>
    /// A TenantAdmin sets the three settings (BR-607): the approval threshold (null for none, otherwise above zero), the
    /// back-date window and the default due days (each 0 to <see cref="MaxDays"/>). Returns false, and records nothing,
    /// when nothing changes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value outside those limits: a bug in the caller, whose validator refuses it first.</exception>
    public bool ChangeSettings(Money? postingApprovalThreshold, int maxBackdateDays, int defaultDueDays)
    {
        if (postingApprovalThreshold is { IsPositive: false })
        {
            throw new ArgumentOutOfRangeException(nameof(postingApprovalThreshold), postingApprovalThreshold, "A threshold is above zero; none is null.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maxBackdateDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxBackdateDays, MaxDays);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultDueDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultDueDays, MaxDays);

        if (postingApprovalThreshold == PostingApprovalThreshold && maxBackdateDays == MaxBackdateDays && defaultDueDays == DefaultDueDays)
        {
            return false;
        }

        PostingApprovalThreshold = postingApprovalThreshold;
        MaxBackdateDays = maxBackdateDays;
        DefaultDueDays = defaultDueDays;
        AddDomainEvent(new TenantSettingsChanged(postingApprovalThreshold, maxBackdateDays, defaultDueDays));

        return true;
    }

    /// <summary>
    /// The approved prefix change of <paramref name="documentType"/> (BR-802, ADR 0033): its default prefix becomes
    /// <paramref name="prefix"/>, which no other document type has as its default.
    /// </summary>
    /// <exception cref="ArgumentException">Not a prefix, or the type has it already: bugs in the caller.</exception>
    /// <exception cref="InvalidOperationException">Another type has it as its default; the use case refuses that first.</exception>
    public void ChangePrefix(DocumentType documentType, string prefix)
    {
        if (!DocumentNumber.IsPrefix(prefix))
        {
            throw new ArgumentException($"A prefix has 1 to {DocumentNumber.PrefixMaxLength} characters, each a capital letter or a digit (BR-802).", nameof(prefix));
        }

        var from = PrefixOf(documentType);
        if (from == prefix)
        {
            throw new ArgumentException("The document type has this prefix already.", nameof(prefix));
        }

        if (Prefixes.Any(other => other.Key != documentType && other.Value == prefix))
        {
            throw new InvalidOperationException($"Prefix {prefix} is the default of another document type; a prefix belongs to one type (ADR 0033).");
        }

        switch (documentType)
        {
            case DocumentType.Bill:
                BillPrefix = prefix;
                break;
            case DocumentType.CreditNote:
                CreditNotePrefix = prefix;
                break;
            case DocumentType.Payment:
                PaymentPrefix = prefix;
                break;
            case DocumentType.Adjustment:
                AdjustmentPrefix = prefix;
                break;
            default:
                RefundPrefix = prefix;
                break;
        }

        AddDomainEvent(new TenantPrefixChanged(documentType, from, prefix));
    }

    /// <summary>
    /// Creates an active tenant with the initial settings: no approval threshold, seven days of back-dating, due on
    /// the bill date, and the standard number prefixes.
    /// </summary>
    /// <param name="code">See <see cref="Code"/>.</param>
    /// <param name="name">See <see cref="Name"/>. Surrounding white space is removed.</param>
    /// <param name="createdAtUtc">The instant of creation, read from the clock by the caller. Must be UTC.</param>
    public static Result<Tenant> Create(string code, string name, DateTimeOffset createdAtUtc)
    {
        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            // A caller's bug, not a business failure: instants come from IClock.UtcNow, which is always UTC.
            throw new ArgumentException("An instant must be in UTC (offset zero).", nameof(createdAtUtc));
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        var fieldErrors = new Dictionary<string, string[]>();

        if (!IsValidCode(code))
        {
            fieldErrors["code"] =
            [
                $"The code must have 1 to {CodeMaxLength} characters: lower-case letters and digits, with single hyphens between them.",
            ];
        }

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            fieldErrors["name"] = [$"The name must have 1 to {NameMaxLength} characters."];
        }

        if (fieldErrors.Count > 0)
        {
            return IdentityErrors.TenantInvalid(fieldErrors);
        }

        return new Tenant(code, trimmedName, createdAtUtc);
    }

    private static bool IsValidCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > CodeMaxLength)
        {
            return false;
        }

        // Starts as true so that a leading hyphen is rejected like a doubled one.
        var previousWasHyphen = true;
        foreach (var character in code)
        {
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
            }
            else if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                previousWasHyphen = false;
            }
            else
            {
                return false;
            }
        }

        return !previousWasHyphen;
    }
}
