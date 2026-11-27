using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The link of a forgotten password on the user (ADR 0061, amended): only its hash is kept, it is good once and for
/// 30 minutes, a later link replaces it, five are sent in an hour, any other change of the password takes it away,
/// and using it ends every session.
/// </summary>
public sealed class PasswordResetLinkTests
{
    private const string OldHash = "pbkdf2-sha256$1$c2FsdA==$b2xk";
    private const string NewHash = "pbkdf2-sha256$1$c2FsdA==$bmV3";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Link = new('a', User.PasswordResetTokenHashLength);
    private static readonly string OtherLink = new('b', User.PasswordResetTokenHashLength);

    private static User NewUser(bool passwordChangeDue = false)
    {
        var user = User.Create("asha.rao@example.in", "Asha Rao", OldHash, [Role.Cashier], Now.AddDays(-1), grantedBy: null, passwordChangeDue: passwordChangeDue);
        user.ClearDomainEvents();

        return user;
    }

    [Fact]
    public void ADR0061_a_link_asked_for_is_kept_as_its_hash_for_30_minutes_and_says_so()
    {
        var user = NewUser();

        user.RequestPasswordReset(Link, Now).ShouldBeTrue();

        (user.PasswordResetTokenHash, user.PasswordResetExpiresAtUtc, user.PasswordResetRequests, user.PasswordResetWindowStartUtc)
            .ShouldBe((Link, Now.AddMinutes(30), 1, (DateTimeOffset?)Now));
        (user.PasswordHash, user.SessionVersion).ShouldBe((OldHash, 0));
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserPasswordResetRequested(user.Id));
    }

    [Fact]
    public void ADR0061_the_link_sets_the_password_once_ends_every_session_and_a_change_that_was_due()
    {
        var user = NewUser(passwordChangeDue: true);
        user.RequestPasswordReset(Link, Now);
        user.ClearDomainEvents();

        user.ResetPasswordByLink(Link, NewHash, Now.AddMinutes(29)).IsSuccess.ShouldBeTrue();

        (user.PasswordHash, user.PasswordChangeDue, user.SessionVersion).ShouldBe((NewHash, false, 1));
        (user.PasswordResetTokenHash, user.PasswordResetExpiresAtUtc).ShouldBe((null, null));
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserPasswordResetByLink(user.Id));

        // Once: the same link again is refused, and nothing changes.
        user.ResetPasswordByLink(Link, OldHash, Now.AddMinutes(29)).Error.Code.ShouldBe(IdentityErrors.PasswordResetLinkInvalidCode);
        (user.PasswordHash, user.SessionVersion).ShouldBe((NewHash, 1));
    }

    [Fact]
    public void ADR0061_the_link_is_refused_at_30_minutes_and_after()
    {
        var user = NewUser();
        user.RequestPasswordReset(Link, Now);

        user.ResetPasswordByLink(Link, NewHash, Now.AddMinutes(30)).Error.Code.ShouldBe(IdentityErrors.PasswordResetLinkInvalidCode);
        user.ResetPasswordByLink(Link, NewHash, Now.AddHours(5)).Error.Code.ShouldBe(IdentityErrors.PasswordResetLinkInvalidCode);
        (user.PasswordHash, user.SessionVersion).ShouldBe((OldHash, 0));

        // A second before, it is good.
        user.ResetPasswordByLink(Link, NewHash, Now.AddMinutes(30).AddSeconds(-1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADR0061_a_wrong_link_no_link_and_a_link_replaced_by_a_later_one_are_all_refused_alike()
    {
        var user = NewUser();
        var none = user.ResetPasswordByLink(Link, NewHash, Now);

        user.RequestPasswordReset(Link, Now);
        var wrong = user.ResetPasswordByLink(OtherLink, NewHash, Now);
        var shorter = user.ResetPasswordByLink("abc", NewHash, Now);

        user.RequestPasswordReset(OtherLink, Now.AddMinutes(1));
        var replaced = user.ResetPasswordByLink(Link, NewHash, Now.AddMinutes(2));

        new[] { none, wrong, shorter, replaced }.ShouldAllBe(result => result.IsFailure && result.Error == IdentityErrors.PasswordResetLinkInvalid());
        (user.PasswordHash, user.SessionVersion).ShouldBe((OldHash, 0));
        user.ResetPasswordByLink(OtherLink, NewHash, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADR0061_five_links_are_sent_in_an_hour_and_the_sixth_changes_nothing()
    {
        var user = NewUser();
        for (var asked = 0; asked < User.PasswordResetRequestsAllowed; asked++)
        {
            user.RequestPasswordReset($"{asked}{Link[1..]}", Now.AddMinutes(asked)).ShouldBeTrue();
        }

        user.ClearDomainEvents();

        user.RequestPasswordReset(OtherLink, Now.AddMinutes(59)).ShouldBeFalse();

        // The fifth link stays the good one, and nothing is said.
        (user.PasswordResetTokenHash, user.PasswordResetRequests).ShouldBe(($"4{Link[1..]}", 5));
        user.DomainEvents.ShouldBeEmpty();

        // An hour after the first, the count begins again.
        user.RequestPasswordReset(OtherLink, Now.AddHours(1)).ShouldBeTrue();
        (user.PasswordResetTokenHash, user.PasswordResetRequests, user.PasswordResetWindowStartUtc).ShouldBe((OtherLink, 1, (DateTimeOffset?)Now.AddHours(1)));
    }

    [Fact]
    public void ADR0061_a_user_who_is_not_active_is_sent_no_link_and_cannot_use_the_one_that_is_out()
    {
        var user = NewUser();
        user.RequestPasswordReset(Link, Now);
        user.Disable();
        user.ClearDomainEvents();

        user.RequestPasswordReset(OtherLink, Now.AddMinutes(1)).ShouldBeFalse();
        user.ResetPasswordByLink(Link, NewHash, Now.AddMinutes(1)).Error.Code.ShouldBe(IdentityErrors.PasswordResetLinkInvalidCode);

        (user.PasswordHash, user.PasswordResetTokenHash).ShouldBe((OldHash, Link));
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ADR0061_a_password_set_any_other_way_takes_the_link_that_is_out_with_it()
    {
        var byAdministrator = NewUser();
        byAdministrator.RequestPasswordReset(Link, Now);
        byAdministrator.ResetPassword(NewHash);
        byAdministrator.ResetPasswordByLink(Link, OldHash, Now.AddMinutes(1)).IsFailure.ShouldBeTrue();
        byAdministrator.PasswordHash.ShouldBe(NewHash);

        var byThemself = NewUser();
        byThemself.RequestPasswordReset(Link, Now);
        byThemself.ChangePassword(NewHash);
        byThemself.ResetPasswordByLink(Link, OldHash, Now.AddMinutes(1)).IsFailure.ShouldBeTrue();
        byThemself.PasswordHash.ShouldBe(NewHash);
    }
}
