using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An entry of the tenant's service catalogue, its chargemaster (docs/domain-model.md, section 4.3): a code that is
/// unique in the tenant, a name, and the price and tax rate a new bill line starts from (BR-201). A bill line copies
/// them and keeps its own copy, so correcting a price here never alters a bill. The code is fixed at creation: it is
/// what price lists and people refer to, and it must not come to mean another service. An item is never deleted, only
/// deactivated. That the code is free in the tenant is the use case's question, and a unique index enforces it.
/// </summary>
public sealed class ServiceItem : AggregateRoot<ServiceItemId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Longest allowed <see cref="Category"/>.</summary>
    public const int CategoryMaxLength = 50;

    /// <summary>The length of <see cref="SacCode"/>: a services accounting code has six digits.</summary>
    public const int SacCodeLength = 6;

    // Also the constructor Entity Framework uses to load an item: it passes the two stored values and then sets the
    // remaining properties.
    private ServiceItem(ServiceItemCode code, string name)
    {
        Code = code;
        Name = name;
        Status = ServiceItemStatus.Active;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The code of the item, unique in the tenant ignoring the case of letters. It never changes.</summary>
    public ServiceItemCode Code { get; private set; }

    /// <summary>The name of the service, as a bill line shows it.</summary>
    public string Name { get; private set; }

    /// <summary>A free grouping for lists and reports, such as Consultation or Laboratory.</summary>
    public string? Category { get; private set; }

    /// <summary>
    /// The services accounting code (SAC) the hospital classifies the service under, six digits, printed on a tax
    /// document (ADR 0054); null when it gave none. A bill line keeps the code its item had when the line was written.
    /// </summary>
    public string? SacCode { get; private set; }

    /// <summary>The price of one unit that a new bill line starts from (BR-201).</summary>
    public UnitPrice DefaultUnitPrice { get; private set; }

    /// <summary>The tax rate that a new bill line starts from (BR-201).</summary>
    public Percent TaxRate { get; private set; }

    /// <summary>Whether the item can be put on new bill lines.</summary>
    public ServiceItemStatus Status { get; private set; }

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

    /// <summary>Creates an active item with <paramref name="code"/>. Whether the code is free in the tenant is the use case's question.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is the default value, or a value of <paramref name="details"/> breaks a limit the validator checks.
    /// </exception>
    public static ServiceItem Create(ServiceItemCode code, ServiceItemDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (code.IsEmpty)
        {
            throw new ArgumentException("A service item needs a code; the default value is not one.", nameof(code));
        }

        var item = new ServiceItem(code, Field.Required(details.Name, NameMaxLength));
        item.Apply(details);

        // The id is still empty here: it is assigned when the item is added to the unit of work.
        item.AddDomainEvent(new ServiceItemCreated(code, item.Name, item.DefaultUnitPrice, item.TaxRate));

        return item;
    }

    /// <summary>
    /// Replaces the name, the category, the price and the tax rate with <paramref name="details"/>: a category left out
    /// is cleared. The code and the status stay as they are, and so does every bill line that was priced from the item.
    /// Raises <see cref="ServiceItemUpdated"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="details"/> breaks a limit the validator checks.</exception>
    public void Update(ServiceItemDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var before = (Name, Category, DefaultUnitPrice, TaxRate, SacCode);
        Apply(details);

        if (before != (Name, Category, DefaultUnitPrice, TaxRate, SacCode))
        {
            AddDomainEvent(new ServiceItemUpdated(Id, Code, Name));
        }
    }

    /// <summary>Deactivates the item: it is no longer offered for new bill lines. Lines that name it are untouched.</summary>
    public Result Deactivate()
    {
        if (Status == ServiceItemStatus.Inactive)
        {
            return BillingErrors.ServiceItemAlreadyInactive();
        }

        Status = ServiceItemStatus.Inactive;
        AddDomainEvent(new ServiceItemDeactivated(Id, Code, Name));

        return Result.Success();
    }

    /// <summary>Makes an inactive item active again.</summary>
    public Result Reactivate()
    {
        if (Status == ServiceItemStatus.Active)
        {
            return BillingErrors.ServiceItemAlreadyActive();
        }

        Status = ServiceItemStatus.Active;
        AddDomainEvent(new ServiceItemReactivated(Id, Code, Name));

        return Result.Success();
    }

    // Every value is checked before any is assigned, so details that are refused leave the item as it was.
    private void Apply(ServiceItemDetails details)
    {
        var name = Field.Required(details.Name, NameMaxLength);
        var category = Field.Optional(details.Category, CategoryMaxLength);
        var sacCode = Field.Optional(details.SacCode, SacCodeLength);
        if (sacCode is not null && !IsSacCode(sacCode))
        {
            throw new ArgumentException($"A SAC code has {SacCodeLength} digits; the validator of the use case should have refused it.", nameof(details));
        }

        Name = name;
        Category = category;
        DefaultUnitPrice = details.DefaultUnitPrice;
        TaxRate = details.TaxRate;
        SacCode = sacCode;
    }

    /// <summary>Whether <paramref name="value"/> has the shape of a SAC code: six digits.</summary>
    public static bool IsSacCode(string? value) => value is { Length: SacCodeLength } && value.All(char.IsAsciiDigit);
}
