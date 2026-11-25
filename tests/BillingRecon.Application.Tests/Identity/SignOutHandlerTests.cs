using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The sign-out with the ports replaced (ADR 0057): the user of the request gets another session number, which refuses
/// every token issued before, the sign-out is one event for the audit trail, and it is committed once.
/// </summary>
public sealed class SignOutHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly User _user;

    public SignOutHandlerTests()
    {
        _user = User.Create("asha.rao@example.in", "Asha Rao", "pbkdf2-sha256$1$c2FsdA==$aGFzaA==", [Role.Cashier], TestDoubles.ClockAt(TestDoubles.Today).UtcNow, grantedBy: null)
            .WithId(new UserId(Guid.NewGuid()));
        _user.ClearDomainEvents();
        _currentUser.UserId.Returns(_user.Id);
        _users.GetAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    [Fact]
    public async Task ADR0057_a_sign_out_ends_the_users_sessions_with_one_event_and_one_commit()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await new SignOutHandler(_currentUser, _users, unitOfWork).HandleAsync(new SignOutCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.SessionVersion.ShouldBe(1);
        _user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserSignedOut(_user.Id));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0057_a_request_without_a_user_or_whose_user_is_gone_signs_nobody_out()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        _currentUser.UserId.Returns((UserId?)null);

        var nobody = await new SignOutHandler(_currentUser, _users, unitOfWork).HandleAsync(new SignOutCommand(), CancellationToken.None);

        var gone = new UserId(Guid.NewGuid());
        _currentUser.UserId.Returns(gone);
        _users.GetAsync(gone, Arg.Any<CancellationToken>()).Returns((User?)null);
        var missing = await new SignOutHandler(_currentUser, _users, unitOfWork).HandleAsync(new SignOutCommand(), CancellationToken.None);

        nobody.Error.Code.ShouldBe(IdentityErrors.UserNotFoundCode);
        missing.Error.Code.ShouldBe(IdentityErrors.UserNotFoundCode);
        _user.SessionVersion.ShouldBe(0);
        await unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task ADR0057_a_sign_out_that_is_not_saved_answers_the_failure()
    {
        var unitOfWork = TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict());

        var result = await new SignOutHandler(_currentUser, _users, unitOfWork).HandleAsync(new SignOutCommand(), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
    }
}
