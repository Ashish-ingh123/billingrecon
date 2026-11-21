using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// One line of a credit note (BR-211): what is credited, how many, at which price, with which discount and tax rate,
/// and the three amounts <see cref="LineCalculator"/> derives from them, exactly as for a line of a bill (BR-202). A
/// line may name the line of the bill it credits; the cap that follows from that is checked when the credit note is
/// submitted and again when it is approved. A line belongs to its credit note and changes only through it; it has no
/// row version of its own.
/// <para>
/// A line of a draft may be deleted (<see cref="IHardDeletable"/>): a draft is not yet a financial record. The lines
/// of a posted credit note never change (BR-006).
/// </para>
/// </summary>
public sealed class CreditNoteLine : Entity<CreditNoteLineId>, ITenantScoped, IHardDeletable
{
    // Entity Framework creates a line with this constructor and then sets the properties.
    private CreditNoteLine()
    {
        Description = string.Empty;
    }

    private CreditNoteLine(int lineNo)
        : this()
    {
        LineNo = lineNo;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The credit note the line belongs to. Set when the credit note is saved.</summary>
    public CreditNoteId CreditNoteId { get; private set; }

    /// <summary>The position of the line on the credit note, from 1.</summary>
    public int LineNo { get; private set; }

    /// <summary>The line of the bill this line credits; null for a line that credits the bill as a whole.</summary>
    public BillLineId? BillLineId { get; private set; }

    /// <summary>What is credited, as the credit note shows it.</summary>
    public string Description { get; private set; }

    /// <summary>How many units, more than zero.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>The price of one unit.</summary>
    public UnitPrice UnitPrice { get; private set; }

    /// <summary>The quantity times the unit price, rounded (BR-202).</summary>
    public Money LineAmount { get; private set; }

    /// <summary>The discount on the line: zero or more, and not more than <see cref="LineAmount"/>.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The tax rate of the line.</summary>
    public Percent TaxRate { get; private set; }

    /// <summary>The tax on the line amount less the discount, rounded (BR-202).</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>The line amount, less the discount, plus the tax (BR-202).</summary>
    public Money LineTotal { get; private set; }

    /// <summary>
    /// The amounts line <paramref name="lineNo"/> would have with <paramref name="details"/>, or the rule that refuses
    /// them. Nothing is changed: the credit note checks every line before it keeps any.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The quantity is the default value of its type, the discount is negative, the bill line id is empty, or the
    /// description breaks a limit the validator checks.
    /// </exception>
    internal static Result<LineAmounts> Check(int lineNo, CreditNoteLineDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNo, 1);

        if (details.Quantity.Value <= 0m)
        {
            throw new ArgumentException("A credit note line needs a quantity above zero; the default value of Quantity is not one.", nameof(details));
        }

        if (details.DiscountAmount.IsNegative)
        {
            throw new ArgumentException("The discount of a credit note line is not negative; the validator of the use case should have refused it.", nameof(details));
        }

        if (details.BillLineId is { } billLineId && billLineId.Value == Guid.Empty)
        {
            throw new ArgumentException("A line that credits a bill line needs its id; leave it null for a line that credits the bill as a whole.", nameof(details));
        }

        _ = Field.Required(details.Description, BillLine.DescriptionMaxLength);

        var amounts = LineCalculator.Calculate(details.Quantity, details.UnitPrice, details.DiscountAmount, details.TaxRate);
        if (amounts.LineAmount.Amount > BillLine.MaxLineAmount)
        {
            return BillingErrors.CreditNoteLineAmountTooLarge(lineNo);
        }

        if (amounts.DiscountAmount > amounts.LineAmount)
        {
            return BillingErrors.CreditNoteLineDiscountExceedsAmount(lineNo);
        }

        return amounts;
    }

    /// <summary>A new line at position <paramref name="lineNo"/> with details and amounts that <see cref="Check"/> accepted.</summary>
    internal static CreditNoteLine Create(int lineNo, CreditNoteLineDetails details, LineAmounts amounts)
    {
        var line = new CreditNoteLine(lineNo);
        line.Apply(details, amounts);

        return line;
    }

    /// <summary>Replaces what the line credits with details and amounts that <see cref="Check"/> accepted. Its position stays.</summary>
    internal void Apply(CreditNoteLineDetails details, LineAmounts amounts)
    {
        BillLineId = details.BillLineId;
        Description = Field.Required(details.Description, BillLine.DescriptionMaxLength);
        Quantity = details.Quantity;
        UnitPrice = details.UnitPrice;
        LineAmount = amounts.LineAmount;
        DiscountAmount = amounts.DiscountAmount;
        TaxRate = details.TaxRate;
        TaxAmount = amounts.TaxAmount;
        LineTotal = amounts.LineTotal;
    }
}
