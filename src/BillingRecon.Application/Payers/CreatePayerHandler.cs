using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Creates a self-pay payer (US-11): for a patient of the tenant who is active and has no payer yet (BR-104), or as a
/// guarantor without a patient. A patient of another tenant is not found, like an id nobody has. The filtered unique
/// index on the patient link decides the race between two payers for one patient, whose loser gets the same answer as
/// the check here.
/// </summary>
public sealed class CreatePayerHandler(
    IValidator<CreatePayerCommand> validator,
    IPatientReadService patients,
    IPayerReadService payers,
    IPayerRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreatePayerCommand, PayerDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerDto>> HandleAsync(CreatePayerCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without a kind, so no request takes this branch; it keeps the compiler honest.
        var kind = command.Kind ?? throw new ArgumentException("The command has no payer kind.", nameof(command));

        // The kind is the first rule (BR-103): it needs nothing from the database.
        var created = Payer.Create(kind, command.ToDetails(), command.PatientId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (command.PatientId is { } patientId)
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

            if (await payers.ExistsForPatientAsync(patientId, ct))
            {
                return PayersErrors.PatientAlreadyLinked();
            }
        }

        var payer = created.Value;
        repository.Add(payer);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The only unique index a new payer can hit is the patient link's: another payer for the patient won the race.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode && command.PatientId is not null
                ? PayersErrors.PatientAlreadyLinked()
                : committed.Error;
        }

        return payer.ToDto();
    }
}
