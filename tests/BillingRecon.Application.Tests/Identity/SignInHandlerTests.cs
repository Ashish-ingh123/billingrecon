using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The sign-in with the ports replaced (US-02; BR-601, BR-602; ADR 0031): the tenant by its code and the user by its
/// address in that tenant, both without regard to case; every refusal costs one password verification and steps into
/// no user, and only the outcome tells the refusals apart, for the log; a success steps into the user, records the
/// sign-in, commits, and only then issues the token.
/// </summary>
public sealed class SignInHandlerTests
{
    private const string Password = "correct horse battery";
    private const string Hash = "pbkdf2-sha256$1$c2FsdA==$aGFzaA==";
    private const string Token = "header.payload.signature";
    private static readonly TenantId Apollo = new(Guid.NewGuid());
    private static readonly SignInCommand Command = new(" Apollo-Delhi ", " Asha.Rao@Example.IN ", Password);

    private readonly ILoginUserLookup _lookup = Substitute.For<ILoginUserLookup>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ISignInScope _scope = Substitute.For<ISignInScope>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly User _user;

    public SignInHandlerTests()
    {
        _user = User.Create("asha.rao@example.in", "Asha Rao", Hash, [Role.Supervisor, Role.Cashier], _clock.UtcNow.AddDays(-30), grantedBy: null);
        _user.ClearDomainEvents();

        _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns(new LoginTenant(Apollo, TenantStatus.Active));
        _lookup.FindUserAsync(Apollo, "ASHA.RAO@EXAMPLE.IN", Arg.Any<CancellationToken>())
            .Returns(new LoginUser(_user.Id, UserStatus.Active, Hash, [Role.Supervisor, Role.Cashier]));
        _hasher.Verify(Password, Hash).Returns(true);
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        // Read before the call is configured: the clock is a substitute too.
        var token = new AccessToken(Token, _clock.UtcNow.AddMinutes(60));
        _tokens.Issue(Apollo, _user.Id, Arg.Any<IReadOnlyCollection<Role>>(), false).Returns(token);
    }

    [Fact]
    public async Task US02_a_user_signs_in_by_the_code_of_the_tenant_and_the_address_in_any_case_and_gets_a_token()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Refusal.ShouldBeNull();
        result.Value.UserId.ShouldBe(_user.Id);
        result.Value.SignedIn.ShouldBe(new SignedInDto(Token, "Bearer", _clock.UtcNow.AddMinutes(60), 3600, false));
    }

    [Fact]
    public async Task BR602_the_request_becomes_the_user_before_it_reads_the_row_and_the_token_comes_after_the_commit()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        Received.InOrder(() =>
        {
            _scope.Enter(Apollo, _user.Id, Arg.Is<IReadOnlyCollection<Role>>(roles => roles.SequenceEqual(new[] { Role.Supervisor, Role.Cashier })));
            _users.GetAsync(_user.Id, Arg.Any<CancellationToken>());
            unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            _tokens.Issue(Apollo, _user.Id, Arg.Is<IReadOnlyCollection<Role>>(roles => roles.SequenceEqual(new[] { Role.Supervisor, Role.Cashier })), false);
        });
    }

    [Fact]
    public async Task US02_the_sign_in_is_recorded_on_the_user_with_the_time_of_the_clock()
    {
        await Handler(TestDoubles.UnitOfWork()).HandleAsync(Command, CancellationToken.None);

        _user.LastSignInAtUtc.ShouldBe(_clock.UtcNow);
        _user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserSignedIn(_user.Id, _clock.UtcNow));
    }

    [Theory]
    [InlineData(SignInRefusal.UnknownTenant)]
    [InlineData(SignInRefusal.TenantSuspended)]
    [InlineData(SignInRefusal.UnknownUser)]
    [InlineData(SignInRefusal.WrongPassword)]
    [InlineData(SignInRefusal.UserDisabled)]
    public async Task BR601_every_refusal_costs_one_password_verification_and_neither_signs_in_nor_commits(SignInRefusal refusal)
    {
        Arrange(refusal);
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("a refusal is an outcome; the API answers every one the same");
        result.Value.SignedIn.ShouldBeNull();
        result.Value.Refusal.ShouldBe(refusal);
        result.Value.UserId.ShouldBe(refusal is SignInRefusal.WrongPassword or SignInRefusal.UserDisabled ? _user.Id : null);

        // The same work whether or not the tenant and the address exist: one derivation of the password.
        _hasher.ReceivedCalls().Count(call => call.GetMethodInfo().Name is nameof(IPasswordHasher.Verify) or nameof(IPasswordHasher.VerifyWithoutHash)).ShouldBe(1);
        _scope.DidNotReceiveWithAnyArgs().Enter(default, default, default!);
        await unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
        _tokens.DidNotReceiveWithAnyArgs().Issue(default, default, default!, default);
        _user.LastSignInAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task BR601_a_disabled_user_is_told_apart_only_after_the_password_is_right()
    {
        // A wrong password of a disabled user is a wrong password: nobody learns that the account is disabled without it.
        Arrange(SignInRefusal.UserDisabled);
        _hasher.Verify(Password, Hash).Returns(false);

        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(Command, CancellationToken.None);

        result.Value.Refusal.ShouldBe(SignInRefusal.WrongPassword);
    }

    [Theory]
    [InlineData("", "asha.rao@example.in", Password)]
    [InlineData("   ", "asha.rao@example.in", Password)]
    [InlineData("apollo-delhi", "", Password)]
    [InlineData("apollo-delhi", "  ", Password)]
    [InlineData("apollo-delhi", "asha.rao@example.in", "")]
    public async Task A_sign_in_without_one_of_its_three_values_is_refused_before_anything_is_looked_up(string tenant, string email, string password)
    {
        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(new SignInCommand(tenant, email, password), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _lookup.DidNotReceiveWithAnyArgs().FindTenantAsync(default!, default);
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_longer_than_any_that_could_match_is_refused_before_anything_is_looked_up()
    {
        SignInCommand[] tooLong =
        [
            Command with { Tenant = new string('a', Tenant.CodeMaxLength + 1) },
            Command with { Email = new string('a', User.EmailMaxLength + 1) },
            // The longest password a user can have is 128 characters (ADR 0030).
            Command with { Password = new string('a', 129) },
        ];

        foreach (var command in tooLong)
        {
            var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(command, CancellationToken.None);

            result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        }

        await _lookup.DidNotReceiveWithAnyArgs().FindTenantAsync(default!, default);
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_code_and_the_address_are_measured_without_the_spaces_around_them()
    {
        // A code of the longest length, typed with spaces around it, is a code that can match.
        var code = new string('a', Tenant.CodeMaxLength);
        var address = new string('a', User.EmailMaxLength);

        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(new SignInCommand($"  {code} ", $" {address}  ", Password), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Refusal.ShouldBe(SignInRefusal.UnknownTenant);
        await _lookup.Received(1).FindTenantAsync(code, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0051_a_user_whose_password_an_administrator_set_gets_a_token_that_only_changes_it()
    {
        _user.ResetPassword(Hash);
        _user.ClearDomainEvents();
        var token = new AccessToken(Token, _clock.UtcNow.AddMinutes(15));
        _tokens.Issue(Apollo, _user.Id, Arg.Any<IReadOnlyCollection<Role>>(), true).Returns(token);

        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(Command, CancellationToken.None);

        result.Value.SignedIn.ShouldBe(new SignedInDto(Token, "Bearer", _clock.UtcNow.AddMinutes(15), 900, true));
    }

    [Fact]
    public async Task A_commit_that_fails_issues_no_token()
    {
        // An administrator changed the user in the same instant: the row version answers, and nobody gets a token.
        var result = await Handler(TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict())).HandleAsync(Command, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        _tokens.DidNotReceiveWithAnyArgs().Issue(default, default, default!, default);
    }

    [Fact]
    public void ADR0031_the_text_of_the_command_and_of_the_answer_carries_no_password_address_or_token()
    {
        var command = Command.ToString();
        var answer = new SignedInDto(Token, "Bearer", _clock.UtcNow, 3600, false).ToString();
        var token = new AccessToken(Token, _clock.UtcNow).ToString();

        command.ShouldContain("Apollo-Delhi");
        command.ShouldNotContain(Password);
        command.ShouldNotContain("Asha.Rao", Case.Insensitive);
        answer.ShouldNotContain(Token);
        answer.ShouldContain("3600");
        token.ShouldNotContain(Token);
    }

    private void Arrange(SignInRefusal refusal)
    {
        switch (refusal)
        {
            case SignInRefusal.UnknownTenant:
                _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns((LoginTenant?)null);
                break;
            case SignInRefusal.TenantSuspended:
                _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns(new LoginTenant(Apollo, TenantStatus.Suspended));
                break;
            case SignInRefusal.UnknownUser:
                _lookup.FindUserAsync(Apollo, "ASHA.RAO@EXAMPLE.IN", Arg.Any<CancellationToken>()).Returns((LoginUser?)null);
                break;
            case SignInRefusal.WrongPassword:
                _hasher.Verify(Password, Hash).Returns(false);
                break;
            case SignInRefusal.UserDisabled:
                _lookup.FindUserAsync(Apollo, "ASHA.RAO@EXAMPLE.IN", Arg.Any<CancellationToken>())
                    .Returns(new LoginUser(_user.Id, UserStatus.Disabled, Hash, [Role.Supervisor, Role.Cashier]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a refusal.");
        }
    }

    private SignInHandler Handler(IUnitOfWork unitOfWork)
        => new(new SignInCommandValidator(), _clock, _lookup, _hasher, _scope, _users, _tokens, unitOfWork);
}
