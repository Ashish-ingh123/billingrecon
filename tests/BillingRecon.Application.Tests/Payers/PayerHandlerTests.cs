using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payers;

/// <summary>
/// The payer use cases with their ports replaced (BR-103 to BR-105, BR-804): what they check, in which order, and that
/// nothing is written when a check fails.
/// </summary>
public sealed class PayerHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];

    private readonly IPatientReadService _patients = Substitute.For<IPatientReadService>();
    private readonly IPayerReadService _payers = Substitute.For<IPayerReadService>();
    private readonly IPayerRepository _repository = Substitute.For<IPayerRepository>();
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    [Fact]
    public async Task BR104_a_guarantor_is_created_without_looking_up_any_patient()
    {
        var result = await Create().HandleAsync(Guarantor(), CancellationToken.None);

        result.Value.PatientId.ShouldBeNull();
        result.Value.Kind.ShouldBe(PayerKind.SelfPay);
        await _patients.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        _repository.Received(1).Add(Arg.Any<Payer>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR104_a_payer_for_an_active_patient_without_one_is_created_and_linked()
    {
        var patient = ActivePatient();

        var result = await Create().HandleAsync(ForPatient(patient.Id), CancellationToken.None);

        result.Value.PatientId.ShouldBe(patient.Id);
        _repository.Received(1).Add(Arg.Is<Payer>(payer => payer.PatientId == new PatientId(patient.Id)));
    }

    [Theory]
    [InlineData(PayerKind.Insurer)]
    [InlineData(PayerKind.Corporate)]
    public async Task BR103_an_insurer_or_corporate_payer_is_refused_with_422_before_anything_is_read(PayerKind kind)
    {
        var result = await Create().HandleAsync(ForPatient(Guid.NewGuid()) with { Kind = kind }, CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PayerKindNotSupportedCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        await _patients.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_payer_without_a_kind_is_refused_with_400()
    {
        var result = await Create().HandleAsync(Guarantor() with { Kind = null }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["kind"]);
    }

    [Fact]
    public async Task A_payer_for_a_patient_the_tenant_does_not_have_is_not_found()
    {
        var result = await Create().HandleAsync(ForPatient(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientNotFoundCode);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_payer_for_an_inactive_patient_is_refused_with_422()
    {
        var patient = Patient.Register(Mrn.Of("MRN-9"), new PatientDetails { FullName = "Priya Sharma" });
        patient.Deactivate();
        var inactive = patient.ToDto() with { Id = Guid.NewGuid() };
        _patients.GetAsync(new PatientId(inactive.Id), Arg.Any<CancellationToken>()).Returns(inactive);

        var result = await Create().HandleAsync(ForPatient(inactive.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientInactiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BR104_a_second_payer_for_a_patient_is_refused_with_409_before_anything_is_written()
    {
        var patient = ActivePatient();
        _payers.ExistsForPatientAsync(new PatientId(patient.Id), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().HandleAsync(ForPatient(patient.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientAlreadyLinkedCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BR104_a_payer_that_loses_the_race_on_the_unique_index_gets_the_same_answer()
    {
        var patient = ActivePatient();
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Create().HandleAsync(ForPatient(patient.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientAlreadyLinkedCode);
    }

    [Fact]
    public async Task A_duplicate_key_for_a_guarantor_is_not_called_a_patient_link_because_a_guarantor_has_none()
    {
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Create().HandleAsync(Guarantor(), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
    }

    [Fact]
    public async Task BR804_correcting_a_payer_from_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var payer = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(payer.Id, OlderVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        payer.Name.ShouldBe("Priya Sharma");
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Correcting_a_payer_from_the_current_version_replaces_its_details_and_commits()
    {
        var payer = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(payer.Id, CurrentVersion) with { Name = "Priya S. Rao" }, CancellationToken.None);

        result.Value.Name.ShouldBe("Priya S. Rao");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Correcting_an_unknown_payer_is_not_found()
    {
        var result = await Update().HandleAsync(Correction(new PayerId(Guid.NewGuid()), CurrentVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PayerNotFoundCode);
    }

    [Fact]
    public async Task BR105_deactivating_a_payer_and_deactivating_it_again()
    {
        var payer = Existing(CurrentVersion);
        var handler = new DeactivatePayerHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new DeactivatePayerCommand(payer.Id, Text(CurrentVersion)), CancellationToken.None)).Value.Status.ShouldBe(PayerStatus.Inactive);
        (await handler.HandleAsync(new DeactivatePayerCommand(payer.Id, Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PayerAlreadyInactiveCode);
        (await handler.HandleAsync(new DeactivatePayerCommand(payer.Id, Text(OlderVersion)), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (await handler.HandleAsync(new DeactivatePayerCommand(new PayerId(Guid.NewGuid()), Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PayerNotFoundCode);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reactivating_an_inactive_payer_and_reactivating_it_again()
    {
        var payer = Existing(CurrentVersion);
        payer.Deactivate();
        var handler = new ReactivatePayerHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new ReactivatePayerCommand(payer.Id, Text(CurrentVersion)), CancellationToken.None)).Value.Status.ShouldBe(PayerStatus.Active);
        (await handler.HandleAsync(new ReactivatePayerCommand(payer.Id, Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PayerAlreadyActiveCode);
    }

    [Fact]
    public async Task Reading_and_listing_payers()
    {
        var known = Existing(CurrentVersion).ToDto();
        _payers.GetAsync(new PayerId(known.Id), Arg.Any<CancellationToken>()).Returns(known);
        var page = new PageOf<PayerDto>([known], 1, 20, 1);
        _payers.ListAsync(Arg.Any<ListPayersQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        (await new GetPayerHandler(_payers).HandleAsync(new GetPayerQuery(new PayerId(known.Id)), CancellationToken.None)).Value.ShouldBe(known);
        (await new GetPayerHandler(_payers).HandleAsync(new GetPayerQuery(new PayerId(Guid.NewGuid())), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PayerNotFoundCode);
        (await new ListPayersHandler(new ListPayersQueryValidator(), _payers).HandleAsync(new ListPayersQuery(), CancellationToken.None)).Value.ShouldBeSameAs(page);
        (await new ListPayersHandler(new ListPayersQueryValidator(), _payers).HandleAsync(new ListPayersQuery { Page = 0 }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
    }

    private CreatePayerHandler Create() => new(new CreatePayerCommandValidator(), _patients, _payers, _repository, _unitOfWork);

    private UpdatePayerHandler Update() => new(new UpdatePayerCommandValidator(), _repository, _unitOfWork);

    private PatientDto ActivePatient()
    {
        var patient = Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" }).ToDto() with { Id = Guid.NewGuid() };
        _patients.GetAsync(new PatientId(patient.Id), Arg.Any<CancellationToken>()).Returns(patient);

        return patient;
    }

    // A payer as the repository would load it: saved, with an id and a row version.
    private Payer Existing(byte[] version)
    {
        var payer = Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Priya Sharma" }, patientId: null).Value.Saved(version);
        var id = new PayerId(Guid.NewGuid());
        typeof(Entity<PayerId>).GetProperty(nameof(Entity<PayerId>.Id))!.SetValue(payer, id);
        _repository.GetAsync(id, Arg.Any<CancellationToken>()).Returns(payer);

        return payer;
    }

    private static string Text(byte[] version) => Convert.ToBase64String(version);

    private static CreatePayerCommand Guarantor() => new() { Kind = PayerKind.SelfPay, Name = "Rahul Sharma" };

    private static CreatePayerCommand ForPatient(Guid patientId) => new() { Kind = PayerKind.SelfPay, PatientId = new PatientId(patientId), Name = "Priya Sharma" };

    private static UpdatePayerCommand Correction(PayerId id, byte[] version) => new() { Id = id, ExpectedVersion = Text(version), Name = "Priya Sharma" };
}
