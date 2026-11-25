using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>A patient the tenant has: its id, its name, whether it is active, and the self-pay payer it is, if it has one.</summary>
public sealed record KnownPatient(PatientId Id, string FullName, bool Active, PayerId? PayerId, bool PayerActive);
