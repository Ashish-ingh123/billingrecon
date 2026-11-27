using System.Text;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The code of the second step (ADR 0061) held to RFC 6238: its own test vectors for SHA-1, the window of one step
/// either way, a code that is good once; and the second step of a user, which is enrolled once, counts its wrong
/// codes, spends its recovery codes and is taken away by an administrator with a hold on the user's decisions.
/// </summary>
public sealed class TotpTests
{
    // The secret of RFC 6238, appendix B, for SHA-1: twenty ASCII bytes.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void RFC6238_the_codes_of_the_rfcs_test_vectors_are_its_last_six_digits(long unixSeconds, string code)
    {
        var instant = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Totp.StepOf(instant).ShouldBe(unixSeconds / 30);
        Totp.CodeOf(RfcSecret, Totp.StepOf(instant)).ShouldBe(code);
        Totp.TryMatch(RfcSecret, code, instant, lastUsedStep: null, out var step).ShouldBeTrue();
        step.ShouldBe(unixSeconds / 30);
    }

    [Fact]
    public void RFC6238_a_code_is_accepted_for_its_step_and_the_one_before_and_after_and_no_further()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var current = Totp.StepOf(now);

        foreach (var away in new[] { -1, 0, 1 })
        {
            Totp.TryMatch(RfcSecret, Totp.CodeOf(RfcSecret, current + away), now, lastUsedStep: null, out var step).ShouldBeTrue($"{away} steps away");
            step.ShouldBe(current + away);
        }

        Totp.TryMatch(RfcSecret, Totp.CodeOf(RfcSecret, current - 2), now, lastUsedStep: null, out _).ShouldBeFalse();
        Totp.TryMatch(RfcSecret, Totp.CodeOf(RfcSecret, current + 2), now, lastUsedStep: null, out _).ShouldBeFalse();

        // The edges of a step: its first and its last second are the same step.
        var first = DateTimeOffset.FromUnixTimeSeconds(current * 30);
        Totp.StepOf(first).ShouldBe(Totp.StepOf(first.AddSeconds(29)));
        Totp.StepOf(first.AddSeconds(30)).ShouldBe(current + 1);
    }

    [Fact]
    public void A_code_is_good_once_the_step_it_was_accepted_for_and_every_earlier_one_are_spent()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var current = Totp.StepOf(now);
        var code = Totp.CodeOf(RfcSecret, current);

        Totp.TryMatch(RfcSecret, code, now, lastUsedStep: current, out _).ShouldBeFalse("the same step again");
        Totp.TryMatch(RfcSecret, Totp.CodeOf(RfcSecret, current - 1), now, lastUsedStep: current, out _).ShouldBeFalse("an earlier step");
        Totp.TryMatch(RfcSecret, Totp.CodeOf(RfcSecret, current + 1), now, lastUsedStep: current, out var next).ShouldBeTrue("the next step is not spent");
        next.ShouldBe(current + 1);
        Totp.TryMatch(RfcSecret, code, now, lastUsedStep: current - 1, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("١٢٣٤٥٦")]
    public void What_is_not_six_ascii_digits_is_no_code(string? code)
        => Totp.TryMatch(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(59), lastUsedStep: null, out _).ShouldBeFalse();

    [Fact]
    public void The_secret_is_given_to_an_app_in_base32_inside_the_address_of_a_qr_code()
    {
        // RFC 4648, section 10.
        Totp.ToBase32(Encoding.ASCII.GetBytes("foobar")).ShouldBe("MZXW6YTBOI");
        Totp.ToBase32(Encoding.ASCII.GetBytes("fo")).ShouldBe("MZXQ");
        Totp.ToBase32(RfcSecret).ShouldBe("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");

        Totp.ProvisioningUri("BillingRecon", "asha+desk@cityhospital.org", RfcSecret).AbsoluteUri.ShouldBe(
            "otpauth://totp/BillingRecon:asha%2Bdesk%40cityhospital.org?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=BillingRecon&algorithm=SHA1&digits=6&period=30");
    }

    [Fact]
    public void ADR0061_a_second_step_is_enrolled_once_and_its_secrets_are_never_in_an_event()
    {
        var user = NewUser();
        var at = new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

        user.ConfirmSecondStep(10, ["a", "b"], at).Error.Code.ShouldBe(IdentityErrors.SecondStepEnrolmentNotBegunCode);
        user.BeginSecondStepEnrolment("v1.first").IsSuccess.ShouldBeTrue();
        user.BeginSecondStepEnrolment("v1.second").IsSuccess.ShouldBeTrue("beginning again replaces a secret that was never confirmed");
        (user.SecondStepEnrolled, user.SecondStepPendingSecret).ShouldBe((false, "v1.second"));
        var sessions = user.SessionVersion;
        user.ClearDomainEvents();

        user.ConfirmSecondStep(10, ["hash-one", "hash-two"], at).IsSuccess.ShouldBeTrue();

        (user.SecondStepEnrolled, user.SecondStepSecret, user.SecondStepPendingSecret, user.SecondStepLastStep, user.SecondStepEnrolledAtUtc, user.RecoveryCodeHashes)
            .ShouldBe((true, "v1.second", null, 10L, at, "hash-one hash-two"));
        user.SessionVersion.ShouldBe(sessions + 1, "every earlier session ends");
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserSecondStepEnrolled(user.Id));
        user.BeginSecondStepEnrolment("v1.third").Error.Code.ShouldBe(IdentityErrors.SecondStepAlreadyEnrolledCode);
        user.ConfirmSecondStep(11, [], at).Error.Code.ShouldBe(IdentityErrors.SecondStepAlreadyEnrolledCode);
    }

    [Fact]
    public void ADR0061_wrong_codes_are_counted_the_fifth_in_a_row_ends_the_sessions_and_a_right_one_starts_the_count_again()
    {
        var user = Enrolled();
        var sessions = user.SessionVersion;

        for (var wrong = 1; wrong < User.SecondStepFailuresAllowed; wrong++)
        {
            user.FailSecondStep().ShouldBeFalse();
        }

        (user.SecondStepFailures, user.SessionVersion).ShouldBe((User.SecondStepFailuresAllowed - 1, sessions));
        user.PassSecondStep(21);
        (user.SecondStepFailures, user.SecondStepLastStep).ShouldBe((0, 21L));

        user.ClearDomainEvents();
        for (var wrong = 1; wrong < User.SecondStepFailuresAllowed; wrong++)
        {
            user.FailSecondStep().ShouldBeFalse();
        }

        user.FailSecondStep().ShouldBeTrue();
        (user.SecondStepFailures, user.SessionVersion).ShouldBe((0, sessions + 1));
        user.DomainEvents.Last().ShouldBe(new UserSecondStepFailed(user.Id, Locked: true));
        user.DomainEvents.Count.ShouldBe(User.SecondStepFailuresAllowed);
    }

    [Fact]
    public void ADR0061_a_recovery_code_is_spent_when_it_is_used_and_no_other_is()
    {
        var user = Enrolled();
        user.FailSecondStep();
        user.ClearDomainEvents();

        user.UseRecoveryCode("hash-two").ShouldBeTrue();

        (user.RecoveryCodeHashes, user.SecondStepFailures).ShouldBe(("hash-one hash-three", 0));
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserRecoveryCodeUsed(user.Id, Remaining: 2));
        user.UseRecoveryCode("hash-two").ShouldBeFalse("a recovery code is good once");
        user.UseRecoveryCode("hash-four").ShouldBeFalse();
        NewUser().UseRecoveryCode("hash-one").ShouldBeFalse("a user without a second step has no recovery codes");
    }

    [Fact]
    public void ADR0061_an_administrators_reset_takes_everything_of_the_second_step_ends_the_sessions_and_holds_the_users_decisions()
    {
        var user = Enrolled();
        var sessions = user.SessionVersion;
        var until = new DateTimeOffset(2026, 11, 29, 6, 0, 0, TimeSpan.Zero);
        user.ClearDomainEvents();

        user.ResetSecondStep(until);

        (user.SecondStepEnrolled, user.SecondStepSecret, user.SecondStepLastStep, user.RecoveryCodeHashes, user.SecondStepEnrolledAtUtc, user.ApprovalHoldUntilUtc)
            .ShouldBe((false, null, null, null, null, until));
        user.SessionVersion.ShouldBe(sessions + 1);
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserSecondStepReset(user.Id));
        user.BeginSecondStepEnrolment("v1.new").IsSuccess.ShouldBeTrue("the user enrols again");

        // A password an administrator resets holds the decisions the same way; one given without a hold keeps the one there is.
        var later = until.AddDays(3);
        user.ResetPassword("hash", later);
        user.ApprovalHoldUntilUtc.ShouldBe(later);
        user.ResetPassword("hash");
        user.ApprovalHoldUntilUtc.ShouldBe(later);
        User.ApprovalHold.ShouldBe(TimeSpan.FromHours(24));
    }

    private static User NewUser()
        => User.Create("meera@cityhospital.org", "Meera Nair", "hash", [Role.Supervisor], new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), grantedBy: null);

    private static User Enrolled()
    {
        var user = NewUser();
        user.BeginSecondStepEnrolment("v1.secret").IsSuccess.ShouldBeTrue();
        user.ConfirmSecondStep(20, ["hash-one", "hash-two", "hash-three"], new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero)).IsSuccess.ShouldBeTrue();
        user.ClearDomainEvents();

        return user;
    }
}
