using System.Reflection;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The tenant's settings and its prefix changes with the ports replaced (US-03; BR-603, BR-607, BR-802; ADR 0033): the
/// settings at the version read and only a change committed; a prefix change refused when it changes nothing, while
/// another waits, or when the prefix belongs to another type, and otherwise opened for a second person; its approval
/// asked again whether the prefix is free, then giving the type the prefix on the tenant and on this year's series.
/// </summary>
public sealed class SettingsHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly UserId Maker = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly PrefixChangeId ChangeId = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPrefixChangeRepository _changes = Substitute.For<IPrefixChangeRepository>();
    private readonly IPrefixUsage _usage = Substitute.For<IPrefixUsage>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly INumberSeriesRepository _series = Substitute.For<INumberSeriesRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly Tenant _tenant = Tenant.Create("city-hospital", "City Hospital", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)).Value.Saved(CurrentVersion);

    public SettingsHandlerTests()
    {
        _tenants.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(_tenant);
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _currentUser.UserId.Returns(Maker);
        _currentUser.Roles.Returns(new[] { Role.TenantAdmin });
        _correlation.CorrelationId.Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task US03_a_tenant_admin_changes_the_settings_at_the_version_read_and_the_change_is_committed()
    {
        var result = await Update().HandleAsync(new UpdateTenantSettingsCommand(50_000m, 3, 15, Version()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.PostingApprovalThreshold, result.Value.MaxBackdateDays, result.Value.DefaultDueDays).ShouldBe((50_000m, 3, 15));
        result.Value.Prefixes.ShouldBe(new DocumentPrefixesDto("BIL", "CN", "PAY", "ADJ", "REF"));
        _tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantSettingsChanged>();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settings_that_change_nothing_commit_nothing_and_a_stale_version_is_refused()
    {
        var same = await Update().HandleAsync(new UpdateTenantSettingsCommand(null, 7, 0, Version()), CancellationToken.None);
        var stale = await Update().HandleAsync(new UpdateTenantSettingsCommand(1m, 7, 0, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208])), CancellationToken.None);

        same.IsSuccess.ShouldBeTrue();
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Theory]
    [InlineData(0d, 7, 0)]
    [InlineData(-1d, 7, 0)]
    [InlineData(10.555, 7, 0)]
    [InlineData(null, -1, 0)]
    [InlineData(null, 366, 0)]
    [InlineData(null, 7, 366)]
    public async Task BR607_a_threshold_that_is_not_above_zero_or_days_outside_0_to_365_are_refused(double? threshold, int maxBackdateDays, int defaultDueDays)
    {
        var result = await Update().HandleAsync(new UpdateTenantSettingsCommand((decimal?)threshold, maxBackdateDays, defaultDueDays, Version()), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task The_two_counts_of_days_are_required()
    {
        var result = await Update().HandleAsync(new UpdateTenantSettingsCommand(null, null, null, Version()), CancellationToken.None);

        result.Error.FieldErrors.Keys.ShouldBe(["maxBackdateDays", "defaultDueDays"], ignoreOrder: true);
    }

    [Fact]
    public async Task BR802_a_change_is_opened_for_a_second_person_with_the_prefix_trimmed_and_upper_cased()
    {
        PrefixChange? added = null;
        _changes.Add(Arg.Do<PrefixChange>(change => added = change.WithId(ChangeId)));
        ApprovalRequest? opened = null;
        _approvals.Add(Arg.Do<ApprovalRequest>(request => opened = request));

        var result = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "  inv ", "New stationery"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.FromPrefix, result.Value.ToPrefix, result.Value.Status).ShouldBe(("BIL", "INV", PrefixChangeStatus.Pending));
        added.ShouldNotBeNull();
        opened.ShouldNotBeNull();
        (opened.DocumentType, opened.DocumentId, opened.Action, opened.MakerUserId, opened.MakerComment)
            .ShouldBe((ApprovalDocumentType.PrefixChange, ChangeId.Value, ApprovalAction.ChangePrefix, Maker, "New stationery"));
        _tenant.BillPrefix.ShouldBe("BIL", "nothing changes before the approval");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_change_to_the_prefix_the_type_has_while_another_waits_or_to_a_prefix_of_another_type_is_refused()
    {
        var unchanged = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "bil", null), CancellationToken.None);

        _changes.AnyPendingAsync(DocumentType.Bill, Arg.Any<CancellationToken>()).Returns(true);
        var waiting = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "INV", null), CancellationToken.None);

        _usage.IsTakenAsync(DocumentType.Payment, "CN", Arg.Any<CancellationToken>()).Returns(true);
        var taken = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Payment, "CN", null), CancellationToken.None);

        unchanged.Error.Code.ShouldBe(IdentityErrors.PrefixUnchangedCode);
        waiting.Error.Code.ShouldBe(IdentityErrors.PrefixChangePendingCode);
        taken.Error.Code.ShouldBe(IdentityErrors.PrefixInUseCode);
        _changes.DidNotReceiveWithAnyArgs().Add(default!);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_change_needs_a_type_a_prefix_of_three_letters_or_digits_and_a_user()
    {
        var noType = await Request().HandleAsync(new RequestPrefixChangeCommand(null, "INV", null), CancellationToken.None);
        var tooLong = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "INVO", null), CancellationToken.None);
        var notLetters = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "I-V", null), CancellationToken.None);
        _currentUser.UserId.Returns((UserId?)null);
        var nobody = await Request().HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "INV", null), CancellationToken.None);

        noType.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        tooLong.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        notLetters.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        nobody.Error.Code.ShouldBe(CommonErrors.ApprovalNeedsUserCode);
    }

    [Fact]
    public async Task A_second_request_in_the_same_instant_is_answered_by_the_unique_index()
    {
        // The repository assigns the id when the change is added, as the unit of work does.
        _changes.Add(Arg.Do<PrefixChange>(change => change.WithId(ChangeId)));

        var result = await Request(TestDoubles.UnitOfWork(CommonErrors.DuplicateKey())).HandleAsync(new RequestPrefixChangeCommand(DocumentType.Bill, "INV", null), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.PrefixChangePendingCode);
    }

    [Fact]
    public async Task BR802_the_approval_gives_the_type_the_prefix_on_the_tenant_and_on_this_years_series()
    {
        var (change, request) = Waiting("BIL", "INV");
        var series = Series("BIL", 124);
        _series.FindAsync(DocumentType.Bill, Fy, Arg.Any<CancellationToken>()).Returns(series);
        AsChecker();

        var result = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Version(), " Checked "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(PrefixChangeStatus.Approved);
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, (UserId?)Checker, "Checked"));
        _tenant.BillPrefix.ShouldBe("INV");
        (series.Prefix, series.NextNumber).ShouldBe(("INV", 124));
        change.DecidedAtUtc.ShouldBe(_clock.UtcNow);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_series_this_year_only_the_default_changes()
    {
        Waiting("CN", "CRN");
        AsChecker();

        var result = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Version(), null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _tenant.CreditNotePrefix.ShouldBe("CRN");
    }

    [Fact]
    public async Task ADR0033_a_prefix_another_type_took_while_the_change_waited_is_refused_at_the_approval()
    {
        var (change, _) = Waiting("BIL", "INV");
        _usage.IsTakenAsync(DocumentType.Bill, "INV", Arg.Any<CancellationToken>()).Returns(true);
        AsChecker();

        var result = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.PrefixInUseCode);
        change.Status.ShouldBe(PrefixChangeStatus.Pending);
        _tenant.BillPrefix.ShouldBe("BIL");
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR603_the_maker_and_a_user_without_a_checkers_role_cannot_decide()
    {
        Waiting("BIL", "INV");

        // The maker, a TenantAdmin.
        var byMaker = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Version(), null), CancellationToken.None);

        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.BillingClerk });
        var byClerk = await Reject().HandleAsync(new RejectPrefixChangeCommand(ChangeId, Version(), "No"), CancellationToken.None);

        byMaker.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        byClerk.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerRoleCode);
        _tenant.BillPrefix.ShouldBe("BIL");
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR605_a_rejection_says_why_closes_the_request_and_changes_no_prefix()
    {
        var (change, request) = Waiting("BIL", "INV");
        AsChecker();

        var noReason = await Reject().HandleAsync(new RejectPrefixChangeCommand(ChangeId, Version(), " "), CancellationToken.None);
        var result = await Reject().HandleAsync(new RejectPrefixChangeCommand(ChangeId, Version(), "Keep BIL on the invoices"), CancellationToken.None);

        noReason.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.IsSuccess.ShouldBeTrue();
        (change.Status, request.Status, request.CheckerComment).ShouldBe((PrefixChangeStatus.Rejected, ApprovalStatus.Rejected, "Keep BIL on the invoices"));
        _tenant.BillPrefix.ShouldBe("BIL");
    }

    [Fact]
    public async Task A_decided_change_or_a_stale_version_is_refused_before_anything_is_decided()
    {
        var (change, _) = Waiting("BIL", "INV");
        AsChecker();
        var stale = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        change.RecordApproval(_clock.UtcNow);
        var decided = await Reject().HandleAsync(new RejectPrefixChangeCommand(ChangeId, Version(), "Too late"), CancellationToken.None);
        _changes.GetAsync(ChangeId, Arg.Any<CancellationToken>()).Returns((PrefixChange?)null);
        var unknown = await Approve().HandleAsync(new ApprovePrefixChangeCommand(ChangeId, Version(), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        decided.Error.Code.ShouldBe(IdentityErrors.PrefixChangeNotPendingCode);
        unknown.Error.Code.ShouldBe(IdentityErrors.PrefixChangeNotFoundCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private (PrefixChange Change, ApprovalRequest Request) Waiting(string from, string to)
    {
        var type = Enum.GetValues<DocumentType>().Single(candidate => _tenant.PrefixOf(candidate) == from);
        var change = PrefixChange.Request(type, from, to).WithId(ChangeId).Saved(CurrentVersion);
        change.ClearDomainEvents();
        var request = ApprovalRequest.Open(ApprovalDocumentType.PrefixChange, ChangeId.Value, ApprovalAction.ChangePrefix, Maker, makerComment: null, _clock.UtcNow, Guid.NewGuid());
        _changes.GetAsync(ChangeId, Arg.Any<CancellationToken>()).Returns(change);
        _approvals.GetPendingAsync(ApprovalDocumentType.PrefixChange, ChangeId.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (change, request);
    }

    private void AsChecker()
    {
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
    }

    // A series as the database holds one: nothing in the application creates it (ADR 0008).
    private static NumberSeries Series(string prefix, int nextNumber)
    {
        var series = (NumberSeries)Activator.CreateInstance(typeof(NumberSeries), BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [prefix], culture: null)!;
        typeof(NumberSeries).GetProperty(nameof(NumberSeries.NextNumber))!.SetValue(series, nextNumber);

        return series;
    }

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private UpdateTenantSettingsHandler Update() => new(new UpdateTenantSettingsCommandValidator(), _tenants, _unitOfWork);

    private RequestPrefixChangeHandler Request(IUnitOfWork? unitOfWork = null)
        => new(new RequestPrefixChangeCommandValidator(), _clock, _currentUser, _correlation, _tenants, _changes, _usage, _approvals, unitOfWork ?? _unitOfWork);

    private ApprovePrefixChangeHandler Approve()
        => new(new ApprovePrefixChangeCommandValidator(), _clock, _currentUser, _changes, _approvals, _tenants, _usage, _series, _unitOfWork);

    private RejectPrefixChangeHandler Reject()
        => new(new RejectPrefixChangeCommandValidator(), _clock, _currentUser, _changes, _approvals, _unitOfWork);
}
