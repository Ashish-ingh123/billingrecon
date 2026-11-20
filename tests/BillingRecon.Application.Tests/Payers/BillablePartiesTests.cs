using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payers;

/// <summary>
/// What the Payers module answers when a bill is about to be raised (BR-102, BR-105, BR-106): the patient and the
/// payer must exist for the tenant and be active, the patient is checked first, and no link between them is required.
/// </summary>
public sealed class BillablePartiesTests
{
    private readonly IPatientReadService _patients = Substitute.For<IPatientReadService>();
    private readonly IPayerReadService _payers = Substitute.For<IPayerReadService>();

    [Fact]
    public async Task BR106_an_active_patient_and_an_active_payer_can_be_billed_whether_or_not_they_are_linked()
    {
        var patient = Patient(active: true);
        var guarantor = Payer(active: true);

        (await Parties().EnsureCanBeBilledAsync(patient, guarantor, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task BR102_a_patient_the_tenant_does_not_have_is_not_found_and_an_inactive_one_cannot_be_billed()
    {
        var payer = Payer(active: true);

        var unknown = await Parties().EnsureCanBeBilledAsync(new PatientId(Guid.NewGuid()), payer, CancellationToken.None);
        var inactive = await Parties().EnsureCanBeBilledAsync(Patient(active: false), payer, CancellationToken.None);

        unknown.Error.Code.ShouldBe(PayersErrors.PatientNotFoundCode);
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
        inactive.Error.Code.ShouldBe(PayersErrors.PatientInactiveCode);
        inactive.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public async Task BR105_a_payer_the_tenant_does_not_have_is_not_found_and_an_inactive_one_cannot_be_given_a_bill()
    {
        var patient = Patient(active: true);

        var unknown = await Parties().EnsureCanBeBilledAsync(patient, new PayerId(Guid.NewGuid()), CancellationToken.None);
        var inactive = await Parties().EnsureCanBeBilledAsync(patient, Payer(active: false), CancellationToken.None);

        unknown.Error.Code.ShouldBe(PayersErrors.PayerNotFoundCode);
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
        inactive.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        inactive.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public async Task The_patient_is_answered_first_and_the_payer_is_not_read_when_the_patient_fails()
    {
        var result = await Parties().EnsureCanBeBilledAsync(Patient(active: false), new PayerId(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientInactiveCode);
        await _payers.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    private BillableParties Parties() => new(_patients, _payers);

    private PatientId Patient(bool active)
    {
        var patient = Domain.Payers.Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" });
        if (!active)
        {
            patient.Deactivate();
        }

        var id = new PatientId(Guid.NewGuid());
        _patients.GetAsync(id, Arg.Any<CancellationToken>()).Returns(patient.ToDto() with { Id = id.Value });

        return id;
    }

    private PayerId Payer(bool active)
    {
        var payer = Domain.Payers.Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value;
        if (!active)
        {
            payer.Deactivate();
        }

        var id = new PayerId(Guid.NewGuid());
        _payers.GetAsync(id, Arg.Any<CancellationToken>()).Returns(payer.ToDto() with { Id = id.Value });

        return id;
    }
}
