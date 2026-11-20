using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Registers a patient (US-10). The MRN must be free in the tenant (BR-101): the check here gives the usual case its
/// answer before anything is written, and the unique index decides the race between two registrations of one MRN,
/// whose loser gets the same answer.
/// </summary>
public sealed class RegisterPatientHandler(
    IValidator<RegisterPatientCommand> validator,
    IPatientReadService patients,
    IPatientRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RegisterPatientCommand, PatientDto>
{
    /// <inheritdoc />
    public async Task<Result<PatientDto>> HandleAsync(RegisterPatientCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var mrn = Mrn.Of(command.Mrn);
        if (await patients.MrnExistsAsync(mrn, ct))
        {
            return PayersErrors.PatientMrnDuplicate();
        }

        var patient = Patient.Register(mrn, command.ToDetails());
        repository.Add(patient);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The only unique index a new patient can hit is the MRN's: another registration won the race.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? PayersErrors.PatientMrnDuplicate() : committed.Error;
        }

        return patient.ToDto();
    }
}
