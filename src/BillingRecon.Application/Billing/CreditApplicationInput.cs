using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>One line of a request to apply credit: an amount of the credit note for one bill (BR-213).</summary>
public sealed record CreditApplicationInput(BillId BillId, decimal? Amount);
