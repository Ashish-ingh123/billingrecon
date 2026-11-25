using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// A user sets a password of their own (ADR 0051): the current password is checked, the new one stored, no change is due
/// any more, and a token without the mark of a due change is issued after the commit; nothing is changed or issued when
/// the current password is wrong or the commit fails, and no text of the command carries a password.
/// </summary>
public sealed class ChangeOwnPasswordHandlerTests
{
    private const string Current = "given by the administrator";
    private const string Chosen = "my own long passphrase";
    private const string Hash = "pbkdf2-sha256$1$c2FsdA==$Z2l2ZW4=";
    private const string NewHash = "pbkdf2-sha256$1$c2FsdA==$b3du";
    private static readonly TenantId Apollo = new(Guid.NewGuid());

    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly User _user;

    public ChangeOwnPasswordHandlerTests()
    {
        _user = User.Create("asha.rao@example.in", "Asha Rao", Hash, [Role.Cashier], _clock.UtcNow.AddDays(-1), grantedBy: null, passwordChangeDue: true)
            .WithId(new UserId(Guid.NewGuid()));
        _user.ClearDomainEvents();
        _tenant.TenantId.Returns(Apollo);
        _currentUser.UserId.Returns(_user.Id);
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _hasher.Verify(Current, Hash).Returns(true);
        _hasher.Hash(Chosen).Returns(NewHash);
        var expires = _clock.UtcNow.AddMinutes(60);
        _tokens.Issue(Apollo, _user.Id, Arg.Any<IReadOnlyCollection<Role>>(), false, Arg.Any<int>()).Returns(new AccessToken("new.token.value", expires));
    }

    private ChangeOwnPasswordHandler Handler(IUnitOfWork unitOfWork)
        => new(new ChangeOwnPasswordCommandValidator(), _tenant, _currentUser, _users, _hasher, _tokens, _clock, unitOfWork);

    [Fact]
    public async Task ADR0051_the_right_current_password_stores_the_new_one_ends_the_due_change_and_issues_a_plain_token()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(new ChangeOwnPasswordCommand(Current, Chosen), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new SignedInDto("new.token.value", "Bearer", _clock.UtcNow.AddMinutes(60), 3600, false));
        _user.PasswordHash.ShouldBe(NewHash);
        _user.PasswordChangeDue.ShouldBeFalse();
        _user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserPasswordChanged(_user.Id));
        Received.InOrder(() =>
        {
            unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            _tokens.Issue(Apollo, _user.Id, Arg.Any<IReadOnlyCollection<Role>>(), false, 1);
        });
    }

    [Fact]
    public async Task ADR0051_a_wrong_current_password_changes_nothing_and_issues_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(new ChangeOwnPasswordCommand("not the password", Chosen), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.PasswordCurrentWrongCode);
        _user.PasswordHash.ShouldBe(Hash);
        _user.PasswordChangeDue.ShouldBeTrue();
        await unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
        _tokens.DidNotReceiveWithAnyArgs().Issue(default, default, default!, default, default);
    }

    [Theory]
    [InlineData("", Chosen)]
    [InlineData(Current, "too short")]
    [InlineData(Current, Current)]
    public async Task ADR0051_a_missing_current_password_a_short_new_one_or_the_same_one_is_refused_before_anything_is_read(string current, string chosen)
    {
        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(new ChangeOwnPasswordCommand(current, chosen), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _users.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task A_commit_that_fails_issues_no_token()
    {
        var result = await Handler(TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict()))
            .HandleAsync(new ChangeOwnPasswordCommand(Current, Chosen), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        _tokens.DidNotReceiveWithAnyArgs().Issue(default, default, default!, default, default);
    }

    [Fact]
    public void ADR0051_backlog_B24_no_text_of_a_command_that_carries_a_password_prints_it()
    {
        new ChangeOwnPasswordCommand(Current, Chosen).ToString().ShouldNotContain(Chosen);
        new ChangeOwnPasswordCommand(Current, Chosen).ToString().ShouldNotContain(Current);
        new ResetUserPasswordCommand(_user.Id, "AAAAAAAAB9E=", Chosen).ToString().ShouldNotContain(Chosen);
        var create = new CreateUserCommand { Email = "a@b.in", DisplayName = "A", Password = Chosen, Roles = [Role.Viewer] }.ToString();
        create.ShouldNotContain(Chosen);
        create.ShouldContain("a@b.in");
    }
}
