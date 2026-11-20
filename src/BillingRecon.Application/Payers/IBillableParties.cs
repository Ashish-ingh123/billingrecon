using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// What the Payers module answers when another module is about to raise a bill (BR-102, BR-105, BR-106): may this
/// patient be billed, and may this payer be given the bill? Other modules ask here and pass the answer on; they never
/// look at the Payers module's entities, statuses or errors themselves (ADR 0003).
/// </summary>
public interface IBillableParties
{
    /// <summary>
    /// Success when the tenant has both, and both are active. Otherwise the failure a client is shown:
    /// <c>patient_not_found</c>, <c>patient_inactive</c>, <c>payer_not_found</c> or <c>payer_inactive</c>, the patient
    /// first. Another tenant's patient or payer is not found, like an id nobody has.
    /// </summary>
    public Task<Result> EnsureCanBeBilledAsync(PatientId patientId, PayerId payerId, CancellationToken ct);

    /// <summary>
    /// Whether <paramref name="payerId"/> can take on what a draft bill comes to when it is posted (BR-105): the payer
    /// exists in the tenant and is active. The patient is not asked about again: the draft exists already, and an
    /// inactive patient only stops new bills (BR-102). Fails with <c>payer_not_found</c> or <c>payer_inactive</c>.
    /// </summary>
    public Task<Result> EnsureCanOweAsync(PayerId payerId, CancellationToken ct);
}
