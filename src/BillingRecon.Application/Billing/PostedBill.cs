using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// What <see cref="IBillSettlement.FindPostedAsync"/> answers: the payer who owes the bill, its bill date, its number,
/// and what is still owed on it. No type of the Billing domain, so that another module can read it (ADR 0003).
/// </summary>
public sealed record PostedBill(PayerId PayerId, DateOnly BillDate, string Number, Money BalanceDue);
