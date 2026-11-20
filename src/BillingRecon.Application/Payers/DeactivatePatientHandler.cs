using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Deactivates a patient (US-12, BR-102). The client must have read the current version (BR-804); a stale version is
/// answered before the status rule, so the client re-reads and sees why.
/// </summary>
public sealed class DeactivatePatientHandler(IPatientRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivatePatientCommand, PatientDto>
{
    /// <inheritdoc />
    public async Task<Result<PatientDto>> HandleAsync(DeactivatePatientCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var patient = await repository.GetAsync(command.Id, ct);
        if (patient is null)
        {
            return PayersErrors.PatientNotFound();
        }

        if (RowVersionText.Of(patient) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var deactivated = patient.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? patient.ToDto() : committed.Error;
    }
}
