using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Replaces the demographics of a patient. The client must have read the current version (BR-804): a stale one is
/// refused before anything changes, and the commit checks the row version once more, against the row itself.
/// </summary>
public sealed class UpdatePatientHandler(
    IValidator<UpdatePatientCommand> validator,
    IPatientRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdatePatientCommand, PatientDto>
{
    /// <inheritdoc />
    public async Task<Result<PatientDto>> HandleAsync(UpdatePatientCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var patient = await repository.GetAsync(command.Id, ct);
        if (patient is null)
        {
            return PayersErrors.PatientNotFound();
        }

        if (RowVersionText.Of(patient) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        patient.UpdateDemographics(command.ToDetails());

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? patient.ToDto() : committed.Error;
    }
}
