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

    /// <summary>Longest allowed line of the letterhead's address, and of its legal name and signatory (ADR 0054).</summary>
    public const int LetterheadLineMaxLength = 200;

    /// <summary>Longest allowed city or state of the letterhead.</summary>
    public const int LetterheadPlaceMaxLength = 100;

    /// <summary>Longest allowed PIN code of the letterhead.</summary>
    public const int LetterheadPinCodeMaxLength = 10;

    /// <summary>Longest allowed phone number of the letterhead.</summary>
    public const int LetterheadPhoneMaxLength = 20;

    /// <summary>Longest allowed e-mail address of the letterhead.</summary>
    public const int LetterheadEmailMaxLength = 256;

    /// <summary>The length of a GSTIN.</summary>
    public const int GstinLength = 15;

    /// <summary>Longest allowed document-number prefix (ADR 0008).</summary>
    public const int PrefixMaxLength = 3;

    /// <summary>Upper limit of <see cref="MaxBackdateDays"/> and <see cref="DefaultDueDays"/> (BR-205).</summary>
    public const int MaxDays = 365;

    /// <summary>The prefix of every opening receivable (ADR 0053): the same for every tenant, and not a setting.</summary>
    public const string OpeningReceivablePrefix = "OBR";

    /// <summary>The prefix of every opening advance (ADR 0053): the same for every tenant, and not a setting.</summary>
    public const string OpeningAdvancePrefix = "OBA";

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

    /// <summary>The legal name printed on the hospital's documents; null to print <see cref="Name"/> (ADR 0054).</summary>
    public string? LegalName { get; private set; }

    /// <summary>The first line of the printed address.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>The second line of the printed address.</summary>
    public string? AddressLine2 { get; private set; }

    /// <summary>The city of the printed address.</summary>
    public string? City { get; private set; }

    /// <summary>The state of the printed address: the place of supply of every document (ADR 0054).</summary>
    public string? State { get; private set; }

    /// <summary>The PIN code of the printed address.</summary>
    public string? PinCode { get; private set; }

    /// <summary>The phone number printed on the hospital's documents.</summary>
    public string? Phone { get; private set; }

    /// <summary>The e-mail address printed on the hospital's documents.</summary>
    public string? Email { get; private set; }

    /// <summary>The hospital's GSTIN, when it is registered: fifteen capital letters and digits.</summary>
    public string? Gstin { get; private set; }

    /// <summary>The line printed under the place of a signature.</summary>
    public string? Signatory { get; private set; }

    /// <summary>The signatory's designation, printed under the name (ADR 0059).</summary>
    public string? SignatoryDesignation { get; private set; }

    /// <summary>The SHA-256 of the logo the hospital prints now; null when it has none (ADR 0059).</summary>
    public string? LogoSha256 { get; private set; }

    /// <summary>The SHA-256 of the signature the hospital prints now; null when it has none (ADR 0059).</summary>
    public string? SignatureSha256 { get; private set; }

    /// <summary>The letterhead as one value.</summary>
    public TenantLetterhead Letterhead => new()
    {
        LegalName = LegalName,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PinCode = PinCode,
        Phone = Phone,
        Email = Email,
        Gstin = Gstin,
        Signatory = Signatory,
        SignatoryDesignation = SignatoryDesignation,
    };

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
        [DocumentType.OpeningReceivable] = OpeningReceivablePrefix,
        [DocumentType.OpeningAdvance] = OpeningAdvancePrefix,
    };

    /// <summary>
    /// Whether the prefix of <paramref name="documentType"/> is a setting of the tenant (BR-802): true for the five kinds of
    /// document a user raises, false for the two kinds of opening balance, whose prefixes are fixed (ADR 0053).
    /// </summary>
    public static bool HasPrefixSetting(DocumentType documentType)
        => documentType is not (DocumentType.OpeningReceivable or DocumentType.OpeningAdvance);

    /// <summary>The default prefix of <paramref name="documentType"/>: the prefix of its next new series (BR-802).</summary>
    public string PrefixOf(DocumentType documentType) => documentType switch
    {
        DocumentType.Bill => BillPrefix,
        DocumentType.CreditNote => CreditNotePrefix,
        DocumentType.Payment => PaymentPrefix,
        DocumentType.Adjustment => AdjustmentPrefix,
        DocumentType.Refund => RefundPrefix,
        DocumentType.OpeningReceivable => OpeningReceivablePrefix,
        DocumentType.OpeningAdvance => OpeningAdvancePrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Not a document type that takes a number."),
    };

    /// <summary>
    /// A TenantAdmin sets what the hospital prints at the head and foot of its documents (ADR 0054). Every part is
    /// trimmed, and a blank one is none. Returns false, and records nothing, when nothing changes.
    /// </summary>
    /// <exception cref="ArgumentException">A text is too long, or the GSTIN is not fifteen capital letters and digits: bugs in the caller, whose validator refuses them first.</exception>
    public bool ChangeLetterhead(TenantLetterhead letterhead)
    {
        ArgumentNullException.ThrowIfNull(letterhead);

        var next = new TenantLetterhead
        {
            LegalName = Field.Optional(letterhead.LegalName, LetterheadLineMaxLength),
            AddressLine1 = Field.Optional(letterhead.AddressLine1, LetterheadLineMaxLength),
            AddressLine2 = Field.Optional(letterhead.AddressLine2, LetterheadLineMaxLength),
            City = Field.Optional(letterhead.City, LetterheadPlaceMaxLength),
            State = Field.Optional(letterhead.State, LetterheadPlaceMaxLength),
            PinCode = Field.Optional(letterhead.PinCode, LetterheadPinCodeMaxLength),
            Phone = Field.Optional(letterhead.Phone, LetterheadPhoneMaxLength),
            Email = Field.Optional(letterhead.Email, LetterheadEmailMaxLength),
            Gstin = Field.Optional(letterhead.Gstin, GstinLength),
            Signatory = Field.Optional(letterhead.Signatory, LetterheadLineMaxLength),
            SignatoryDesignation = Field.Optional(letterhead.SignatoryDesignation, LetterheadLineMaxLength),
        };

        if (next.Gstin is { } gstin && !IsGstin(gstin))
        {
            throw new ArgumentException($"A GSTIN has {GstinLength} characters, each a capital letter or a digit.", nameof(letterhead));
        }

        if (next == Letterhead)
        {
            return false;
        }

        LegalName = next.LegalName;
        AddressLine1 = next.AddressLine1;
        AddressLine2 = next.AddressLine2;
        City = next.City;
        State = next.State;
        PinCode = next.PinCode;
        Phone = next.Phone;
        Email = next.Email;
        Gstin = next.Gstin;
        Signatory = next.Signatory;
        SignatoryDesignation = next.SignatoryDesignation;
        AddDomainEvent(new TenantLetterheadChanged(LegalName, State, Gstin));

        return true;
    }

    /// <summary>The hash of the image of <paramref name="kind"/> the hospital prints now; null when it has none.</summary>
    public string? ImageSha256Of(LetterheadImageKind kind) => kind switch
    {
        LetterheadImageKind.Logo => LogoSha256,
        LetterheadImageKind.Signature => SignatureSha256,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not one the enum defines."),
    };

    /// <summary>
    /// A TenantAdmin sets, replaces or removes an image of the letterhead (ADR 0059): the tenant names the image by its
    /// SHA-256, and null removes it. Returns false, and records nothing, when the hash is the one it has.
    /// </summary>
    /// <exception cref="ArgumentException">The hash is not 64 lower-case hexadecimal characters: a bug in the caller.</exception>
    public bool ChangeLetterheadImage(LetterheadImageKind kind, string? sha256)
    {
        if (sha256 is not null && (sha256.Length != TenantImage.Sha256Length || !sha256.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
        {
            throw new ArgumentException("A SHA-256 has 64 lower-case hexadecimal characters.", nameof(sha256));
        }

        var previous = ImageSha256Of(kind);
        if (previous == sha256)
        {
            return false;
        }

        if (kind == LetterheadImageKind.Logo)
        {
            LogoSha256 = sha256;
        }
        else
        {
            SignatureSha256 = sha256;
        }

        AddDomainEvent(new TenantLetterheadImageChanged(kind, previous, sha256));

        return true;
    }

    /// <summary>Whether <paramref name="value"/> has the shape of a GSTIN: fifteen capital letters and digits.</summary>
    public static bool IsGstin(string? value)
        => value is { Length: GstinLength } && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character));

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
        if (!HasPrefixSetting(documentType))
        {
            throw new ArgumentException("The prefix of an opening balance is the same for every tenant and is not changed (ADR 0053).", nameof(documentType));
        }

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

    /// <summary>Longest allowed reason for a suspension or its lifting.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>
    /// An operator suspends the tenant (ADR 0052): nobody of it signs in, and a token issued before is refused. Nothing
    /// of it is deleted. Returns false, and records nothing, when it is suspended already.
    /// </summary>
    /// <exception cref="ArgumentException">No reason, or one too long: a bug in the caller, which checks it first.</exception>
    public bool Suspend(string reason) => ChangeStatus(TenantStatus.Suspended, reason);

    /// <summary>An operator lifts the suspension (ADR 0052). Returns false, and records nothing, when the tenant is active.</summary>
    /// <exception cref="ArgumentException">No reason, or one too long: a bug in the caller, which checks it first.</exception>
    public bool Resume(string reason) => ChangeStatus(TenantStatus.Active, reason);

    private bool ChangeStatus(TenantStatus status, string reason)
    {
        var given = reason?.Trim() ?? string.Empty;
        if (given.Length is 0 or > ReasonMaxLength)
        {
            throw new ArgumentException($"A reason has 1 to {ReasonMaxLength} characters.", nameof(reason));
        }

        if (Status == status)
        {
            return false;
        }

        Status = status;
        AddDomainEvent(status == TenantStatus.Suspended ? new TenantSuspended(given) : new TenantResumed(given));

        return true;
    }

    /// <summary>
    /// A tenant made by the operator's onboarding command (ADR 0052): as <see cref="Create"/>, and recorded as
    /// <see cref="TenantOnboarded"/>, so that its audit trail begins with its creation.
    /// </summary>
    public static Result<Tenant> Onboard(string code, string name, DateTimeOffset createdAtUtc)
    {
        var created = Create(code, name, createdAtUtc);
        if (created.IsSuccess)
        {
            created.Value.AddDomainEvent(new TenantOnboarded(created.Value.Code, created.Value.Name));
        }

        return created;
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
