using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// Which token a user is given (ADR 0061): a password that is due is changed first; then a user with a second step
/// gives its code; then a user whose roles need one enrols; and only then is the token a full one. One place decides
/// it for the sign-in, the changed password and the second step.
/// </summary>
public sealed class SignInTokensTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly DateTimeOffset At = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly ISecondStepPolicy _policy = Substitute.For<ISecondStepPolicy>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private SecondStepStage? _issued;
    private bool? _issuedWithPasswordDue;

    public SignInTokensTests()
    {
        _policy.IsRequiredFor(Arg.Any<IReadOnlyCollection<Role>>())
            .Returns(call => call.Arg<IReadOnlyCollection<Role>>().Contains(Role.Supervisor) || call.Arg<IReadOnlyCollection<Role>>().Contains(Role.TenantAdmin));
        _tokens.Issue(Tenant, Arg.Any<UserId>(), Arg.Any<IReadOnlyCollection<Role>>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<SecondStepStage>())
            .Returns(call =>
            {
                _issuedWithPasswordDue = call.ArgAt<bool>(3);
                _issued = call.ArgAt<SecondStepStage>(5);

                return new AccessToken("token", _clock.UtcNow.AddMinutes(60));
            });
    }

    [Theory]
    [InlineData(Role.Cashier, false, false, SecondStepStage.None)]
    [InlineData(Role.Cashier, true, false, SecondStepStage.Pending)]
    [InlineData(Role.Cashier, true, true, SecondStepStage.Done)]
    [InlineData(Role.Supervisor, false, false, SecondStepStage.Enrolment)]
    [InlineData(Role.TenantAdmin, false, false, SecondStepStage.Enrolment)]
    [InlineData(Role.Supervisor, true, false, SecondStepStage.Pending)]
    [InlineData(Role.Supervisor, true, true, SecondStepStage.Done)]
    public void ADR0061_the_stage_follows_the_users_second_step_their_roles_and_what_this_request_proved(Role role, bool enrolled, bool given, SecondStepStage stage)
    {
        var user = NewUser(role, passwordChangeDue: false, enrolled);

        var answer = SignInTokens.For(_tokens, _policy, _clock, Tenant, user, given);

        (answer.SecondStep, _issued, answer.CredentialsChangeDue, answer.ExpiresInSeconds).ShouldBe((stage, stage, false, 3600));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ADR0051_a_password_that_is_due_is_changed_before_any_second_step(bool enrolled, bool given)
    {
        var user = NewUser(Role.Supervisor, passwordChangeDue: true, enrolled);

        var answer = SignInTokens.For(_tokens, _policy, _clock, Tenant, user, given);

        (answer.CredentialsChangeDue, _issuedWithPasswordDue, answer.SecondStep, _issued).ShouldBe((true, true, SecondStepStage.None, SecondStepStage.None));
    }

    [Fact]
    public void The_answer_carries_the_token_and_never_prints_it()
    {
        var answer = SignInTokens.For(_tokens, _policy, _clock, Tenant, NewUser(Role.Supervisor, passwordChangeDue: false, enrolled: true), secondStepGiven: true);

        answer.AccessToken.ShouldBe("token");
        answer.ToString().ShouldNotContain("token = token", Case.Insensitive);
        answer.ToString().ShouldContain("SecondStep = Done");
    }

    private static User NewUser(Role role, bool passwordChangeDue, bool enrolled)
    {
        var user = User.Create("meera@cityhospital.org", "Meera Nair", "hash", [role], At, grantedBy: null, passwordChangeDue);
        if (enrolled)
        {
            user.BeginSecondStepEnrolment("v1.secret").IsSuccess.ShouldBeTrue();
            user.ConfirmSecondStep(1, ["hash-one"], At).IsSuccess.ShouldBeTrue();
        }

        return user;
    }
}
