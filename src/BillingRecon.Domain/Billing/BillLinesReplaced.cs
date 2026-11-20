using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The lines of a draft bill were replaced, and with them its totals. Raised only when a line really changed.</summary>
public sealed record BillLinesReplaced(BillId BillId, int LineCount, Money Total) : IDomainEvent;
