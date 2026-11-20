using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>A draft bill was voided (BR-204, US-23): it keeps its content, and has no number and no ledger effect.</summary>
public sealed record BillVoided(BillId BillId, PatientId PatientId, PayerId PayerId, Money Total, string Reason) : IDomainEvent, IHasReason;
