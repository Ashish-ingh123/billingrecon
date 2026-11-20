using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// The amounts of one line as <see cref="LineCalculator"/> computed them (BR-202). Every one is money, exact to the
/// paisa; the identities <c>TaxableAmount = LineAmount - DiscountAmount</c> and
/// <c>LineTotal = TaxableAmount + TaxAmount</c> hold by construction.
/// </summary>
public readonly record struct LineAmounts(Money LineAmount, Money DiscountAmount, Money TaxableAmount, Money TaxAmount, Money LineTotal);
