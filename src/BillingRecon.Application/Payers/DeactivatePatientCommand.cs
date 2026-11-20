using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>Deactivates a patient (US-12, BR-102). <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record DeactivatePatientCommand(PatientId Id, string ExpectedVersion);
