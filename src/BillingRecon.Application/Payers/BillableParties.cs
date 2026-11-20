using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Answers <see cref="IBillableParties"/> from the module's own read services. Any active payer of the tenant may owe a
/// bill for any active patient of the tenant: BR-106 asks for one patient and one payer, not for a link between them
/// (a guarantor has none, and a parent who is a patient themself may pay for a child).
/// </summary>
public sealed class BillableParties(IPatientReadService patients, IPayerReadService payers) : IBillableParties
{
    /// <inheritdoc />
    public async Task<Result> EnsureCanBeBilledAsync(PatientId patientId, PayerId payerId, CancellationToken ct)
    {
        var patient = await patients.GetAsync(patientId, ct);
        if (patient is null)
        {
            return PayersErrors.PatientNotFound();
        }

        if (patient.Status != PatientStatus.Active)
        {
            return PayersErrors.PatientInactive();
        }

        var payer = await payers.GetAsync(payerId, ct);
        if (payer is null)
        {
            return PayersErrors.PayerNotFound();
        }

        return payer.Status == PayerStatus.Active ? Result.Success() : PayersErrors.PayerInactive();
    }
}
