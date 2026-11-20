using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// A test-only stand-in for a financial POST, to prove the idempotency filter before a real posting exists
/// (Prompt 3.2). Like a posting it takes a gapless document number and saves a record in one unit of work; the record
/// is a patient, because that is something the application database can hold. What the action does after that is
/// chosen by the request, so that every outcome of the filter can be provoked: succeed, be refused, fault, or wait.
/// </summary>
[Route(ApiRoutes.V1 + "/probe/idempotent")]
public sealed class IdempotencyProbeController(
    IdempotencyProbeState state,
    INumberSeries numberSeries,
    IPatientRepository patients,
    IUnitOfWork unitOfWork,
    IClock clock) : ApiControllerBase
{
    public const string RefusedCode = "probe_refused";

    public const string FaultMessage = "probe-fault-after-save";

    private const string GetRoute = "GetIdempotencyProbe";

    /// <summary>Takes a payment number, registers a patient with <c>mrn</c>, commits, and then does what <c>then</c> says.</summary>
    [HttpPost("records")]
    [Idempotent]
    public async Task<IActionResult> CreateAsync(IdempotencyProbeRequest request, [FromQuery] string? tag, CancellationToken ct)
    {
        state.CountExecution();

        if (request.Then == IdempotencyProbeOutcome.Wait)
        {
            await state.WaitAtGateAsync(ct);
        }

        var number = await numberSeries.NextAsync(DocumentType.Payment, FiscalYear.FromDate(clock.TodayIst), ct);
        var patient = Patient.Register(Mrn.Of(request.Mrn), new PatientDetails { FullName = "Idempotency Probe" });
        patients.Add(patient);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return Problem(committed.Error);
        }

        switch (request.Then)
        {
            case IdempotencyProbeOutcome.Refuse:
                // A refusal after the number was taken and the record saved: all of it has to be undone.
                return Problem(Error.BusinessRule(RefusedCode, "The probe was told to refuse."));
            case IdempotencyProbeOutcome.Fault:
                throw new InvalidOperationException(FaultMessage);
            case IdempotencyProbeOutcome.FaultOnce when state.TakeFault():
                throw new InvalidOperationException(FaultMessage);
            default:
                var response = new IdempotencyProbeResponse(patient.Id.Value, request.Mrn, number.Value, tag, state.Executions);

                return CreatedWithVersion(GetRoute, patient.Id.Value, response, RowVersionText.Of(patient));
        }
    }

    /// <summary>A second idempotent action, so that one key can be tried on two paths.</summary>
    [HttpPost("other")]
    [Idempotent]
    public IActionResult Other(IdempotencyProbeRequest request)
    {
        state.CountExecution();

        return Ok(new IdempotencyProbeResponse(Guid.Empty, request.Mrn, string.Empty, null, state.Executions));
    }

    /// <summary>The address a created record is said to have; nothing reads it.</summary>
    [HttpGet("records/{id:guid}", Name = GetRoute)]
    public IActionResult Get(Guid id) => Ok(id);
}
