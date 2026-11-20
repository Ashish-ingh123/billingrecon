using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>
/// Who owes a bill and pays it (BR-103 to BR-106). In R1 every payer is self-pay: a patient paying their own bills, or
/// a guarantor paying for someone, with no patient link. A patient has at most one payer linked to them (BR-104); the
/// use case checks it and a filtered unique index enforces it. The kind and the patient link are fixed at creation, so
/// what a payer owes never moves to another person. A payer is never deleted, only deactivated (BR-105). Its balance
/// is not part of it: that is a cache of the receivables ledger (docs/domain-model.md, section 4.10).
/// </summary>
public sealed class Payer : AggregateRoot<PayerId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Longest allowed <see cref="Phone"/>.</summary>
    public const int PhoneMaxLength = 20;

    /// <summary>Longest allowed <see cref="Email"/>.</summary>
    public const int EmailMaxLength = 256;

    // Also the constructor Entity Framework uses to load a payer: it passes the two stored values and then sets the
    // remaining properties.
    private Payer(PayerKind kind, string name)
    {
        Kind = kind;
        Name = name;
        Status = PayerStatus.Active;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Who the payer is (BR-103). Always <see cref="PayerKind.SelfPay"/> in R1.</summary>
    public PayerKind Kind { get; private set; }

    /// <summary>The name of the payer: the patient's own name, or the guarantor's.</summary>
    public string Name { get; private set; }

    /// <summary>The patient this payer is, when it is one (BR-104); null for a guarantor. It never changes.</summary>
    public PatientId? PatientId { get; private set; }

    /// <summary>A phone number.</summary>
    public string? Phone { get; private set; }

    /// <summary>An e-mail address.</summary>
    public string? Email { get; private set; }

    /// <summary>Whether the payer can take on new bills, payments and refunds (BR-105).</summary>
    public PayerStatus Status { get; private set; }

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
    /// Creates an active payer of <paramref name="kind"/>, linked to <paramref name="patientId"/> or, without one, a
    /// guarantor. Only self-pay payers exist in R1: the reserved kinds are refused with <c>payer_kind_not_supported</c>
    /// (BR-103). Whether the patient exists in the tenant, is active and has no payer yet is the use case's question
    /// (BR-104): it needs other aggregates, which this one does not see.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="details"/> breaks a limit the validator checks.</exception>
    public static Result<Payer> Create(PayerKind kind, PayerDetails details, PatientId? patientId)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The payer kind is not one the enum defines.");
        }

        if (patientId is { } linked && linked.Value == Guid.Empty)
        {
            throw new ArgumentException("A patient link needs a patient id; leave it null for a guarantor.", nameof(patientId));
        }

        if (kind != PayerKind.SelfPay)
        {
            return PayersErrors.PayerKindNotSupported();
        }

        var payer = new Payer(kind, Field.Required(details.Name, NameMaxLength)) { PatientId = patientId };
        payer.Apply(details);

        // The id is still empty here: it is assigned when the payer is added to the unit of work.
        payer.AddDomainEvent(new PayerCreated(kind, payer.Name, patientId));

        return payer;
    }

    /// <summary>
    /// Replaces the name and contact details with <paramref name="details"/>: a contact detail left out is cleared. The
    /// kind, the patient link and the status stay as they are. Raises <see cref="PayerUpdated"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="details"/> breaks a limit the validator checks.</exception>
    public void Update(PayerDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var before = (Name, Phone, Email);
        Apply(details);

        if (before != (Name, Phone, Email))
        {
            AddDomainEvent(new PayerUpdated(Id, Kind, Name));
        }
    }

    /// <summary>
    /// Deactivates the payer (BR-105): no new bill, payment or refund for it. Its existing balance remains and can still
    /// be allocated and reported.
    /// </summary>
    public Result Deactivate()
    {
        if (Status == PayerStatus.Inactive)
        {
            return PayersErrors.PayerAlreadyInactive();
        }

        Status = PayerStatus.Inactive;
        AddDomainEvent(new PayerDeactivated(Id, Kind, Name));

        return Result.Success();
    }

    /// <summary>Makes an inactive payer active again.</summary>
    public Result Reactivate()
    {
        if (Status == PayerStatus.Active)
        {
            return PayersErrors.PayerAlreadyActive();
        }

        Status = PayerStatus.Active;
        AddDomainEvent(new PayerReactivated(Id, Kind, Name));

        return Result.Success();
    }

    // Every value is checked before any is assigned, so details that are refused leave the payer as it was.
    private void Apply(PayerDetails details)
    {
        var name = Field.Required(details.Name, NameMaxLength);
        var phone = Field.Optional(details.Phone, PhoneMaxLength);
        var email = Field.Optional(details.Email, EmailMaxLength);

        Name = name;
        Phone = phone;
        Email = email;
    }
}
