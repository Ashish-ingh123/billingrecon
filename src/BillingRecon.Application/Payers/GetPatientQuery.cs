using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>Reads one patient of the tenant.</summary>
public sealed record GetPatientQuery(PatientId Id);
