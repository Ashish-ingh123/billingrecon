using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// Asking for the link of a forgotten password with the ports replaced (ADR 0061, amended): the answer is the same
/// whatever happened; a mail goes to the address the hospital keeps, after the commit and only then; and with no
/// mail server nothing is read at all.
/// </summary>
public sealed class RequestPasswordResetHandlerTests
{
    private const string Hash = "pbkdf2-sha256$1$c2FsdA==$aGFzaA==";
    private const string Token = "the-token-of-the-link";
    private static readonly string TokenHash = new('c', User.PasswordResetTokenHashLength);
    private static readonly TenantId Apollo = new(Guid.NewGuid());
    private static readonly RequestPasswordResetCommand Command = new(" Apollo-Delhi ", " asha.rao@EXAMPLE.in ");

    private readonly ILoginUserLookup _lookup = Substitute.For<ILoginUserLookup>();
    private readonly ISignInScope _scope = Substitute.For<ISignInScope>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokens _tokens = Substitute.For<IPasswordResetTokens>();
    private readonly IPasswordResetMail _mail = Substitute.For<IPasswordResetMail>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly User _user;

    public RequestPasswordResetHandlerTests()
    {
        _user = User.Create("Asha.Rao@example.in", "Asha Rao", Hash, [Role.Cashier], _clock.UtcNow.AddDays(-30), grantedBy: null);
        _user.ClearDomainEvents();

        _mail.IsConfigured.Returns(true);
        _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns(new LoginTenant(Apollo, TenantStatus.Active));
        _lookup.FindUserAsync(Apollo, "ASHA.RAO@EXAMPLE.IN", Arg.Any<CancellationToken>())
            .Returns(new LoginUser(_user.Id, UserStatus.Active, Hash, [Role.Cashier]));
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _tokens.New(_user.Id).Returns((Token, TokenHash));
    }

    private RequestPasswordResetHandler Handler(IUnitOfWork unitOfWork)
        => new(new RequestPasswordResetCommandValidator(), _clock, _lookup, _scope, _users, _tokens, _mail, unitOfWork);

    [Fact]
    public async Task ADR0061_a_known_user_gets_the_hash_stored_and_the_mail_at_the_address_the_hospital_keeps()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.Value.ShouldBeTrue();
        (_user.PasswordResetTokenHash, _user.PasswordResetExpiresAtUtc).ShouldBe((TokenHash, _clock.UtcNow.AddMinutes(30)));
        _scope.Received(1).Enter(Apollo, _user.Id, Arg.Any<IReadOnlyCollection<Role>>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        // Not the address that was typed: the one on the user, and the code of the hospital as it is kept.
        _mail.Received(1).Send("Asha.Rao@example.in", "Asha Rao", "apollo-delhi", Token);
    }

    [Fact]
    public async Task ADR0061_an_unknown_hospital_an_unknown_address_and_a_disabled_user_answer_the_same_and_send_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var handler = Handler(unitOfWork);

        var noHospital = await handler.HandleAsync(Command with { Tenant = "nowhere" }, CancellationToken.None);
        var noUser = await handler.HandleAsync(Command with { Email = "nobody@example.in" }, CancellationToken.None);
        _lookup.FindUserAsync(Apollo, "ASHA.RAO@EXAMPLE.IN", Arg.Any<CancellationToken>())
            .Returns(new LoginUser(_user.Id, UserStatus.Disabled, Hash, [Role.Cashier]));
        var disabled = await handler.HandleAsync(Command, CancellationToken.None);
        _lookup.FindTenantAsync("apollo-delhi", Arg.Any<CancellationToken>()).Returns(new LoginTenant(Apollo, TenantStatus.Suspended));
        var suspended = await handler.HandleAsync(Command, CancellationToken.None);

        new[] { noHospital, noUser, disabled, suspended }.ShouldAllBe(result => result.IsSuccess && result.Value);
        _scope.DidNotReceiveWithAnyArgs().Enter(default, default, default!);
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        _mail.DidNotReceiveWithAnyArgs().Send(default!, default!, default!, default!);
        _user.PasswordResetTokenHash.ShouldBeNull();
    }

    [Fact]
    public async Task ADR0061_the_sixth_link_of_the_hour_answers_the_same_and_sends_nothing()
    {
        for (var asked = 0; asked < User.PasswordResetRequestsAllowed; asked++)
        {
            _user.RequestPasswordReset($"{asked}{TokenHash[1..]}", _clock.UtcNow.AddMinutes(-10));
        }

        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.Value.ShouldBeTrue();
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        _mail.DidNotReceiveWithAnyArgs().Send(default!, default!, default!, default!);
        _user.PasswordResetTokenHash.ShouldBe($"4{TokenHash[1..]}");
    }

    [Fact]
    public async Task ADR0061_a_commit_that_fails_sends_no_mail_and_answers_the_same()
    {
        var unitOfWork = TestDoubles.UnitOfWork(Error.Conflict("concurrency_conflict", "The user was changed."));

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.Value.ShouldBeTrue();
        _mail.DidNotReceiveWithAnyArgs().Send(default!, default!, default!, default!);
    }

    [Fact]
    public async Task ADR0061_without_a_mail_server_nothing_is_read_and_the_answer_is_the_same()
    {
        _mail.IsConfigured.Returns(false);
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Handler(unitOfWork).HandleAsync(Command, CancellationToken.None);

        result.Value.ShouldBeTrue();
        await _lookup.DidNotReceiveWithAnyArgs().FindTenantAsync(default!, default);
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        _mail.DidNotReceiveWithAnyArgs().Send(default!, default!, default!, default!);
    }

    [Theory]
    [InlineData("", "asha.rao@example.in")]
    [InlineData("apollo-delhi", "")]
    public async Task A_request_without_a_hospital_or_an_address_is_refused_before_anything_is_read(string tenant, string email)
    {
        var result = await Handler(TestDoubles.UnitOfWork()).HandleAsync(new RequestPasswordResetCommand(tenant, email), CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _lookup.DidNotReceiveWithAnyArgs().FindTenantAsync(default!, default);
    }
}
