using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>Makes an inactive patient active again. The client must have read the current version (BR-804).</summary>
public sealed class ReactivatePatientHandler(IPatientRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<ReactivatePatientCommand, PatientDto>
{
    /// <inheritdoc />
    public async Task<Result<PatientDto>> HandleAsync(ReactivatePatientCommand command, CancellationToken ct)
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

        var reactivated = patient.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? patient.ToDto() : committed.Error;
    }
}
