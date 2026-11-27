using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The user use cases with the ports replaced (US-01; BR-601; ADR 0030): a free address, a hashed password, roles
/// granted by the current user, the guards of the last administrator and of oneself, and nothing committed after a
/// refusal.
/// </summary>
public sealed class UserHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly UserId Admin = new(Guid.NewGuid());
    private static readonly UserId Id = new(Guid.NewGuid());

    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public UserHandlerTests()
    {
        _currentUser.UserId.Returns(Admin);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hashed:{call.Arg<string>().Length}");
    }

    [Fact]
    public async Task US01_a_user_is_created_with_the_hash_of_the_password_and_roles_granted_by_the_current_user()
    {
        User? added = null;
        _repository.Add(Arg.Do<User>(user => added = user));

        var result = await Create().HandleAsync(NewUser(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Email, result.Value.Status).ShouldBe(("asha.rao@example.in", UserStatus.Active));
        result.Value.Roles.ShouldBe([Role.Supervisor, Role.Cashier]);
        added.ShouldNotBeNull();
        added.PasswordHash.ShouldBe("hashed:22", "the password itself is never kept");
        added.Roles.ShouldAllBe(role => role.GrantedByUserId == Admin && role.GrantedAtUtc == _clock.UtcNow);
        await _repository.Received(1).EmailTakenAsync("ASHA.RAO@EXAMPLE.IN", null, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR601_an_address_another_user_has_is_refused_before_and_after_the_save()
    {
        _repository.EmailTakenAsync("ASHA.RAO@EXAMPLE.IN", null, Arg.Any<CancellationToken>()).Returns(true);

        var taken = await Create().HandleAsync(NewUser(), CancellationToken.None);

        taken.Error.Code.ShouldBe(IdentityErrors.UserEmailTakenCode);
        taken.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        _hasher.DidNotReceiveWithAnyArgs().Hash(default!);

        // Another request took it between the question and the save: the unique index answers.
        _repository.EmailTakenAsync("ASHA.RAO@EXAMPLE.IN", null, Arg.Any<CancellationToken>()).Returns(false);
        var raced = await new CreateUserHandler(new CreateUserCommandValidator(), _clock, _currentUser, _hasher, _repository, TestDoubles.UnitOfWork(CommonErrors.DuplicateKey()))
            .HandleAsync(NewUser(), CancellationToken.None);
        raced.Error.Code.ShouldBe(IdentityErrors.UserEmailTakenCode);
    }

    [Fact]
    public async Task A_password_shorter_than_twelve_characters_and_a_user_without_a_role_are_refused_by_the_validator()
    {
        var shortPassword = await Create().HandleAsync(NewUser() with { Password = "elevenchars" }, CancellationToken.None);
        var noRole = await Create().HandleAsync(NewUser() with { Roles = [] }, CancellationToken.None);
        var noAddress = await Create().HandleAsync(NewUser() with { Email = "asha.rao" }, CancellationToken.None);

        shortPassword.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        noRole.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        noAddress.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task US01_an_address_change_is_refused_when_another_user_has_it_and_a_stale_or_unknown_user_is_refused()
    {
        var user = Existing(Role.Cashier);
        _repository.EmailTakenAsync("TAKEN@EXAMPLE.IN", Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateUserHandler(new UpdateUserCommandValidator(), _repository, _unitOfWork);

        var taken = await handler.HandleAsync(new UpdateUserCommand(Id, Version(), "taken@example.in", "Asha"), CancellationToken.None);
        var stale = await handler.HandleAsync(new UpdateUserCommand(Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), "asha@example.in", "Asha"), CancellationToken.None);
        var unknown = await handler.HandleAsync(new UpdateUserCommand(new UserId(Guid.NewGuid()), Version(), "asha@example.in", "Asha"), CancellationToken.None);
        var changed = await handler.HandleAsync(new UpdateUserCommand(Id, Version(), "asha@hospital.in", "Dr Asha Rao"), CancellationToken.None);

        taken.Error.Code.ShouldBe(IdentityErrors.UserEmailTakenCode);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(IdentityErrors.UserNotFoundCode);
        (changed.Value.Email, changed.Value.DisplayName).ShouldBe(("asha@hospital.in", "Dr Asha Rao"));
        user.NormalizedEmail.ShouldBe("ASHA@HOSPITAL.IN");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0030_the_last_active_administrator_keeps_the_role_and_another_administrator_lets_it_go()
    {
        var user = Existing(Role.TenantAdmin);
        var handler = new ReplaceUserRolesHandler(new ReplaceUserRolesCommandValidator(), _clock, _currentUser, _repository, _unitOfWork);

        var last = await handler.HandleAsync(new ReplaceUserRolesCommand(Id, Version(), [Role.Supervisor]), CancellationToken.None);
        last.Error.Code.ShouldBe(IdentityErrors.UserLastTenantAdminCode);
        last.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        user.HasRole(Role.TenantAdmin).ShouldBeTrue();

        _repository.CountOtherActiveTenantAdminsAsync(Id, Arg.Any<CancellationToken>()).Returns(1);
        var replaced = await handler.HandleAsync(new ReplaceUserRolesCommand(Id, Version(), [Role.Supervisor]), CancellationToken.None);

        replaced.Value.Roles.ShouldBe([Role.Supervisor]);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0030_roles_that_keep_the_administrator_or_belong_to_another_user_need_no_count()
    {
        var handler = new ReplaceUserRolesHandler(new ReplaceUserRolesCommandValidator(), _clock, _currentUser, _repository, _unitOfWork);

        Existing(Role.TenantAdmin);
        (await handler.HandleAsync(new ReplaceUserRolesCommand(Id, Version(), [Role.TenantAdmin, Role.Cashier]), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        Existing(Role.Cashier);
        (await handler.HandleAsync(new ReplaceUserRolesCommand(Id, Version(), [Role.Viewer]), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await _repository.DidNotReceiveWithAnyArgs().CountOtherActiveTenantAdminsAsync(default, default);
    }

    [Fact]
    public async Task ADR0030_nobody_disables_themselves_or_the_last_active_administrator()
    {
        var handler = new DisableUserHandler(_currentUser, _repository, _unitOfWork);
        var user = Existing(Role.TenantAdmin);

        _currentUser.UserId.Returns(Id);
        var self = await handler.HandleAsync(new DisableUserCommand(Id, Version()), CancellationToken.None);
        _currentUser.UserId.Returns(Admin);
        var last = await handler.HandleAsync(new DisableUserCommand(Id, Version()), CancellationToken.None);

        self.Error.Code.ShouldBe(IdentityErrors.UserCannotDisableSelfCode);
        last.Error.Code.ShouldBe(IdentityErrors.UserLastTenantAdminCode);
        user.Status.ShouldBe(UserStatus.Active);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);

        _repository.CountOtherActiveTenantAdminsAsync(Id, Arg.Any<CancellationToken>()).Returns(1);
        var disabled = await handler.HandleAsync(new DisableUserCommand(Id, Version()), CancellationToken.None);
        var again = await handler.HandleAsync(new DisableUserCommand(Id, Version()), CancellationToken.None);

        disabled.Value.Status.ShouldBe(UserStatus.Disabled);
        again.Error.Code.ShouldBe(IdentityErrors.UserAlreadyDisabledCode);
    }

    [Fact]
    public async Task BR601_a_disabled_user_is_enabled_and_an_active_one_is_refused()
    {
        var user = Existing(Role.Viewer);
        var handler = new EnableUserHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new EnableUserCommand(Id, Version()), CancellationToken.None)).Error.Code.ShouldBe(IdentityErrors.UserAlreadyActiveCode);
        user.Disable().IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new EnableUserCommand(Id, Version()), CancellationToken.None)).Value.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task ADR0030_a_password_reset_keeps_the_hash_of_the_new_password_and_never_the_password()
    {
        var user = Existing(Role.Cashier);
        var handler = new ResetUserPasswordHandler(new ResetUserPasswordCommandValidator(), _hasher, _clock, _repository, _unitOfWork);

        var tooShort = await handler.HandleAsync(new ResetUserPasswordCommand(Id, Version(), "short"), CancellationToken.None);
        var reset = await handler.HandleAsync(new ResetUserPasswordCommand(Id, Version(), "a new long passphrase"), CancellationToken.None);

        tooShort.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        reset.IsSuccess.ShouldBeTrue();
        user.PasswordHash.ShouldBe("hashed:21");
        _hasher.Received(1).Hash("a new long passphrase");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    private CreateUserHandler Create() => new(new CreateUserCommandValidator(), _clock, _currentUser, _hasher, _repository, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private static CreateUserCommand NewUser() => new()
    {
        Email = " asha.rao@example.in ",
        DisplayName = "Asha Rao",
        Password = "a long enough passphrase"[..22],
        Roles = [Role.Cashier, Role.Supervisor],
    };

    // A user of the tenant, as the repository loads it.
    private User Existing(params Role[] roles)
    {
        var user = User.Create("asha.rao@example.in", "Asha Rao", "hash", roles, _clock.UtcNow, Admin).WithId(Id).Saved(CurrentVersion);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(user);

        return user;
    }
}
