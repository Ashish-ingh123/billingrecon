using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// The one place where the arithmetic of a line lives (BR-202; docs/domain-model.md, section 3), used by bill lines
/// and, later, credit note lines:
/// <code>
/// LineAmount    = round(Quantity x UnitPrice)
/// TaxableAmount = LineAmount - DiscountAmount
/// TaxAmount     = round(TaxableAmount x TaxRate / 100)
/// LineTotal     = TaxableAmount + TaxAmount
/// </code>
/// Each product is rounded once, to the paisa and away from zero, through <see cref="Money.Round"/> (BR-001); the sums
/// are sums of money and are never rounded again. The database does not repeat the multiplications, because SQL
/// Server rounds differently in edge cases: this is the single implementation.
/// </summary>
public static class LineCalculator
{
    /// <summary>The amount of the line before discount and tax: the quantity times the unit price, rounded.</summary>
    public static Money LineAmount(Quantity quantity, UnitPrice unitPrice) => Money.Round(quantity.Value * unitPrice.Value);

    /// <summary>The tax on a taxable amount at a rate in percent, rounded.</summary>
    public static Money TaxAmount(Money taxableAmount, Percent taxRate) => Money.Round(taxableAmount.Amount * taxRate.Value / 100m);

    /// <summary>
    /// Every amount of a line. The discount is taken as given: that it is not negative and not more than the line
    /// amount is the rule of the line (BR-201), which the caller checks before it keeps the result.
    /// </summary>
    public static LineAmounts Calculate(Quantity quantity, UnitPrice unitPrice, Money discountAmount, Percent taxRate)
    {
        var lineAmount = LineAmount(quantity, unitPrice);
        var taxableAmount = lineAmount - discountAmount;
        var taxAmount = TaxAmount(taxableAmount, taxRate);

        return new LineAmounts(lineAmount, discountAmount, taxableAmount, taxAmount, taxableAmount + taxAmount);
    }
}
