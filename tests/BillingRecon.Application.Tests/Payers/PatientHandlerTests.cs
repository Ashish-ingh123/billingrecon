using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payers;

/// <summary>
/// The patient use cases with their ports replaced (BR-101, BR-102, BR-804): what they check, in which order, and that
/// nothing is written when a check fails.
/// </summary>
public sealed class PatientHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];

    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IPatientReadService _reads = Substitute.For<IPatientReadService>();
    private readonly IPatientRepository _repository = Substitute.For<IPatientRepository>();
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    [Fact]
    public async Task BR101_registering_adds_the_patient_and_commits_once()
    {
        var result = await Register().HandleAsync(Registration(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Mrn.ShouldBe("MRN-000123");
        result.Value.FullName.ShouldBe("Priya Sharma");
        result.Value.Status.ShouldBe(PatientStatus.Active);
        _repository.Received(1).Add(Arg.Is<Patient>(patient => patient.Mrn == Mrn.Of("MRN-000123")));
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_registration_is_refused_before_anything_is_read_or_written()
    {
        var result = await Register().HandleAsync(Registration() with { Mrn = string.Empty }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _reads.DidNotReceiveWithAnyArgs().MrnExistsAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR101_an_mrn_the_tenant_already_has_is_refused_with_409_before_anything_is_written()
    {
        _reads.MrnExistsAsync(Mrn.Of("MRN-000123"), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Register().HandleAsync(Registration(), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientMrnDuplicateCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR101_a_registration_that_loses_the_race_on_the_unique_index_gets_the_same_answer()
    {
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Register().HandleAsync(Registration(), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientMrnDuplicateCode);
    }

    [Fact]
    public async Task Correcting_an_unknown_patient_is_not_found()
    {
        var result = await Update().HandleAsync(Correction(Guid.NewGuid(), CurrentVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientNotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task BR804_correcting_from_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var patient = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(patient.Id.Value, OlderVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        patient.FullName.ShouldBe("Priya Sharma");
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Correcting_from_the_current_version_replaces_the_demographics_and_commits()
    {
        var patient = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(patient.Id.Value, CurrentVersion) with { FullName = "Priya S. Rao" }, CancellationToken.None);

        result.Value.FullName.ShouldBe("Priya S. Rao");
        patient.FullName.ShouldBe("Priya S. Rao");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR804_a_conflict_the_commit_finds_is_passed_on_unchanged()
    {
        var patient = Existing(CurrentVersion);
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict());

        var result = await Update().HandleAsync(Correction(patient.Id.Value, CurrentVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task An_invalid_correction_is_refused_before_the_patient_is_loaded()
    {
        var result = await Update().HandleAsync(Correction(Guid.NewGuid(), CurrentVersion) with { FullName = " " }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task BR102_deactivating_from_the_current_version_makes_the_patient_inactive_and_commits()
    {
        var patient = Existing(CurrentVersion);

        var result = await Deactivate().HandleAsync(new DeactivatePatientCommand(patient.Id, Text(CurrentVersion)), CancellationToken.None);

        result.Value.Status.ShouldBe(PatientStatus.Inactive);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR102_deactivating_an_inactive_patient_is_refused_with_422_and_nothing_is_committed()
    {
        var patient = Existing(CurrentVersion);
        patient.Deactivate();

        var result = await Deactivate().HandleAsync(new DeactivatePatientCommand(patient.Id, Text(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientAlreadyInactiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR804_a_stale_version_is_answered_before_the_status_rule()
    {
        var patient = Existing(CurrentVersion);
        patient.Deactivate();

        var result = await Deactivate().HandleAsync(new DeactivatePatientCommand(patient.Id, Text(OlderVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task Deactivating_an_unknown_patient_is_not_found()
    {
        var result = await Deactivate().HandleAsync(new DeactivatePatientCommand(new PatientId(Guid.NewGuid()), Text(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PatientNotFoundCode);
    }

    [Fact]
    public async Task Reactivating_an_inactive_patient_makes_them_active_and_reactivating_an_active_one_is_refused()
    {
        var patient = Existing(CurrentVersion);
        patient.Deactivate();
        var handler = new ReactivatePatientHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new ReactivatePatientCommand(patient.Id, Text(CurrentVersion)), CancellationToken.None)).Value.Status.ShouldBe(PatientStatus.Active);
        (await handler.HandleAsync(new ReactivatePatientCommand(patient.Id, Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PatientAlreadyActiveCode);
    }

    [Fact]
    public async Task Reading_an_unknown_patient_is_not_found_and_a_known_one_is_returned()
    {
        var known = Existing(CurrentVersion).ToDto();
        _reads.GetAsync(new PatientId(known.Id), Arg.Any<CancellationToken>()).Returns(known);
        var handler = new GetPatientHandler(_reads);

        (await handler.HandleAsync(new GetPatientQuery(new PatientId(known.Id)), CancellationToken.None)).Value.ShouldBe(known);
        (await handler.HandleAsync(new GetPatientQuery(new PatientId(Guid.NewGuid())), CancellationToken.None)).Error.Code.ShouldBe(PayersErrors.PatientNotFoundCode);
    }

    [Fact]
    public async Task Listing_checks_the_query_first_and_then_returns_the_page_of_the_read_service()
    {
        var page = new PageOf<PatientDto>([], 1, 20, 0);
        _reads.ListAsync(Arg.Any<ListPatientsQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var handler = new ListPatientsHandler(new ListPatientsQueryValidator(), _reads);

        (await handler.HandleAsync(new ListPatientsQuery(), CancellationToken.None)).Value.ShouldBeSameAs(page);
        (await handler.HandleAsync(new ListPatientsQuery { PageSize = 0 }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _reads.Received(1).ListAsync(Arg.Any<ListPatientsQuery>(), Arg.Any<CancellationToken>());
    }

    private RegisterPatientHandler Register() => new(new RegisterPatientCommandValidator(_clock), _reads, _repository, _unitOfWork);

    private UpdatePatientHandler Update() => new(new UpdatePatientCommandValidator(_clock), _repository, _unitOfWork);

    private DeactivatePatientHandler Deactivate() => new(_repository, _unitOfWork);

    // A patient as the repository would load it: saved, with an id and a row version.
    private Patient Existing(byte[] version)
    {
        var patient = Patient.Register(Mrn.Of("MRN-000123"), new PatientDetails { FullName = "Priya Sharma" }).Saved(version);
        var id = new PatientId(Guid.NewGuid());
        typeof(Entity<PatientId>).GetProperty(nameof(Entity<PatientId>.Id))!.SetValue(patient, id);
        patient.ClearDomainEvents();
        _repository.GetAsync(id, Arg.Any<CancellationToken>()).Returns(patient);

        return patient;
    }

    private static string Text(byte[] version) => Convert.ToBase64String(version);

    private static RegisterPatientCommand Registration() => new() { Mrn = "MRN-000123", FullName = "Priya Sharma", City = "Pune" };

    private static UpdatePatientCommand Correction(Guid id, byte[] version) => new()
    {
        Id = new PatientId(id),
        ExpectedVersion = Text(version),
        FullName = "Priya Sharma",
    };
}
