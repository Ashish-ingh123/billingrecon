using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>A draft bill at or above the threshold of the tenant was handed to a second person (BR-207). It has no number yet.</summary>
public sealed record BillSubmittedForApproval(BillId BillId, PayerId PayerId, Money Total) : IDomainEvent;
