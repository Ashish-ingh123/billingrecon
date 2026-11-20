using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// One line of a bill (BR-201, BR-202): what is charged, how many, at which price, with which discount and tax rate,
/// and the three amounts <see cref="LineCalculator"/> derives from them. A line belongs to its bill and changes only
/// through it; it has no row version of its own, the bill's covers it. A line priced from the service catalogue keeps
/// its own description, price and tax rate, so a later change of the catalogue never alters a bill.
/// <para>
/// A line of a draft may be deleted (<see cref="IHardDeletable"/>): a draft is not yet a financial record. Once bills
/// can be posted, a posted bill's lines never change (BR-006).
/// </para>
/// </summary>
public sealed class BillLine : Entity<BillLineId>, ITenantScoped, IHardDeletable
{
    /// <summary>Longest allowed <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 300;

    /// <summary>
    /// The largest <see cref="LineAmount"/> a line may have. With at most <see cref="Bill.MaxLines"/> lines, the totals
    /// of a bill stay far inside what a <c>decimal(18,2)</c> column holds.
    /// </summary>
    public const decimal MaxLineAmount = 9_999_999_999.99m;

    // Entity Framework creates a line with this constructor and then sets the properties.
    private BillLine()
    {
        Description = string.Empty;
    }

    private BillLine(int lineNo)
        : this()
    {
        LineNo = lineNo;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The bill the line belongs to. Set when the bill is saved.</summary>
    public BillId BillId { get; private set; }

    /// <summary>The position of the line on the bill, from 1.</summary>
    public int LineNo { get; private set; }

    /// <summary>The catalogue entry the line was priced from; null for a free-text line.</summary>
    public ServiceItemId? ServiceItemId { get; private set; }

    /// <summary>What is charged, as the bill shows it.</summary>
    public string Description { get; private set; }

    /// <summary>How many units, more than zero.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>The price of one unit.</summary>
    public UnitPrice UnitPrice { get; private set; }

    /// <summary>The quantity times the unit price, rounded (BR-202).</summary>
    public Money LineAmount { get; private set; }

    /// <summary>The discount on the line: zero or more, and not more than <see cref="LineAmount"/> (BR-201).</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The tax rate of the line.</summary>
    public Percent TaxRate { get; private set; }

    /// <summary>The tax on the line amount less the discount, rounded (BR-202).</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>The line amount, less the discount, plus the tax (BR-202).</summary>
    public Money LineTotal { get; private set; }

    /// <summary>
    /// The amounts line <paramref name="lineNo"/> would have with <paramref name="details"/>, or the rule of BR-201
    /// that refuses them. Nothing is changed: the bill checks every line before it keeps any.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The quantity is the default value of its type, the discount is negative, or the description breaks a limit the
    /// validator checks.
    /// </exception>
    internal static Result<LineAmounts> Check(int lineNo, BillLineDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNo, 1);

        if (details.Quantity.Value <= 0m)
        {
            throw new ArgumentException("A bill line needs a quantity above zero; the default value of Quantity is not one.", nameof(details));
        }

        if (details.DiscountAmount.IsNegative)
        {
            throw new ArgumentException("The discount of a bill line is not negative; the validator of the use case should have refused it.", nameof(details));
        }

        if (details.ServiceItemId is { } itemId && itemId.Value == Guid.Empty)
        {
            throw new ArgumentException("A line priced from the catalogue needs a service item id; leave it null for a free-text line.", nameof(details));
        }

        _ = Field.Required(details.Description, DescriptionMaxLength);

        var amounts = LineCalculator.Calculate(details.Quantity, details.UnitPrice, details.DiscountAmount, details.TaxRate);
        if (amounts.LineAmount.Amount > MaxLineAmount)
        {
            return BillingErrors.BillLineAmountTooLarge(lineNo);
        }

        if (amounts.DiscountAmount > amounts.LineAmount)
        {
            return BillingErrors.BillLineDiscountExceedsAmount(lineNo);
        }

        return amounts;
    }

    /// <summary>A new line at position <paramref name="lineNo"/> with details and amounts that <see cref="Check"/> accepted.</summary>
    internal static BillLine Create(int lineNo, BillLineDetails details, LineAmounts amounts)
    {
        var line = new BillLine(lineNo);
        line.Apply(details, amounts);

        return line;
    }

    /// <summary>Replaces what the line charges with details and amounts that <see cref="Check"/> accepted. Its position stays.</summary>
    internal void Apply(BillLineDetails details, LineAmounts amounts)
    {
        ServiceItemId = details.ServiceItemId;
        Description = Field.Required(details.Description, DescriptionMaxLength);
        Quantity = details.Quantity;
        UnitPrice = details.UnitPrice;
        LineAmount = amounts.LineAmount;
        DiscountAmount = amounts.DiscountAmount;
        TaxRate = details.TaxRate;
        TaxAmount = amounts.TaxAmount;
        LineTotal = amounts.LineTotal;
    }
}
