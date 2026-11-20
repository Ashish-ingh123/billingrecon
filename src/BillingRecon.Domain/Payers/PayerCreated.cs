using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>
/// A payer was created (BR-103, BR-104), linked to a patient or, without one, a guarantor. It carries no payer id: the
/// id is assigned when the payer is added to the unit of work, after the event was raised (ADR 0015, section 2).
/// </summary>
public sealed record PayerCreated(PayerKind Kind, string Name, PatientId? PatientId) : IDomainEvent;
