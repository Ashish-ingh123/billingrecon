using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// Setting a password with the link of a mail, the ports replaced (ADR 0061, amended): the link names its user, the
/// request acts as that user with no role, one refusal covers everything that is not the good link, and no text of
/// the command carries the link or the password.
/// </summary>
public sealed class ResetPasswordByLinkHandlerTests
{
    private const string OldHash = "pbkdf2-sha256$1$c2FsdA==$b2xk";
    private const string NewHash = "pbkdf2-sha256$1$c2FsdA==$bmV3";
    private const string Token = "the-token-of-the-link";
    private const string Chosen = "a new passphrase of mine";
    private static readonly string TokenHash = new('c', User.PasswordResetTokenHashLength);
    private static readonly TenantId Apollo = new(Guid.NewGuid());
    private static readonly ResetPasswordByLinkCommand Command = new(" Apollo-Delhi ", Token, Chosen);

    private readonly ILoginUserLookup _lookup = Substitute.For<ILoginUserLookup>();
    private readonly ISignInScope _scope = Substitute.For<ISignInScope>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokens _tokens = Substitute.For<IPasswordResetTokens>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly User _user;

    public ResetPasswordByLinkHandlerTests()
    {
        _user = User.Create("asha.rao@example.in", "Asha Rao", OldHash, [Role.Supervisor], _clock.UtcNow.AddDays(-30), grantedBy: null);
        _user.RequestPasswordReset(TokenHash, _clock.UtcNow.AddMinutes(-5));
        _user.ClearDomainEvents();

        _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns(new LoginTenant(Apollo, TenantStatus.Active));
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _hasher.Hash(Chosen).Returns(NewHash);
        _tokens.TryRead(Token, out Arg.Any<UserId>(), out Arg.Any<string>()).Returns(call =>
        {
            call[1] = _user.Id;
            call[2] = TokenHash;

            return true;
        });
    }

    private ResetPasswordByLinkHandler Handler(IUnitOfWork unitOfWork)
        => new(new ResetPasswordByLinkCommandValidator(), _clock, _lookup, _scope, _users, _tokens, _hasher, unitOfWork);

    [Fact]
    public async Task ADR0061_the_good_link_stores_the_new_password_as_the_user_it_names_with_no_role()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (_user.PasswordHash, _user.SessionVersion, _user.PasswordResetTokenHash).ShouldBe((NewHash, 1, null));
        _scope.Received(1).Enter(Apollo, _user.Id, Arg.Is<IReadOnlyCollection<Role>>(roles => roles.Count == 0));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0061_an_unknown_hospital_a_text_that_is_no_link_and_an_unknown_user_are_refused_alike_and_write_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var handler = Handler(unitOfWork);

        var noHospital = await handler.HandleAsync(Command with { Tenant = "nowhere" }, CancellationToken.None);
        var noLink = await handler.HandleAsync(Command with { Token = "something else" }, CancellationToken.None);
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);
        var noUser = await handler.HandleAsync(Command, CancellationToken.None);

        new[] { noHospital, noLink, noUser }.ShouldAllBe(result => result.IsFailure && result.Error == IdentityErrors.PasswordResetLinkInvalid());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        _user.PasswordHash.ShouldBe(OldHash);
    }

    [Fact]
    public async Task ADR0061_a_link_that_is_too_old_is_refused_and_writes_nothing()
    {
        _user.RequestPasswordReset(TokenHash, _clock.UtcNow.AddMinutes(-31));
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.PasswordResetLinkInvalidCode);
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", Token, Chosen)]
    [InlineData("apollo-delhi", "", Chosen)]
    [InlineData("apollo-delhi", Token, "too short")]
    public async Task A_request_without_its_parts_or_with_a_short_password_is_refused_before_anything_is_read(string tenant, string token, string password)
    {
        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(new ResetPasswordByLinkCommand(tenant, token, password), CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _lookup.DidNotReceiveWithAnyArgs().FindTenantAsync(default!, default);
    }

    [Fact]
    public void No_text_of_the_command_carries_the_link_or_the_password()
    {
        var text = Command.ToString();

        text.ShouldNotContain(Token);
        text.ShouldNotContain(Chosen);
    }
}
