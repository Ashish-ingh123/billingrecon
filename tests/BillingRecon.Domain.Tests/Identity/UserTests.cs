using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The user aggregate (US-01, US-02; BR-601; ADR 0030, ADR 0031): an address compared without regard to case, at least
/// one role, roles replaced with their grants kept, disable and enable, a password reset that says so without the hash,
/// and a sign-in recorded for an active user only.
/// </summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = CreatedAt.AddDays(3);
    private static readonly UserId Admin = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3a20"));
    private static readonly UserId OtherAdmin = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3a21"));

    [Fact]
    public void US01_a_user_is_created_active_with_the_address_trimmed_and_normalised_and_each_role_once()
    {
        var user = User.Create("  Asha.Rao@Example.IN  ", "  Asha Rao ", "pbkdf2-sha256$1$c2FsdA==$aGFzaA==", [Role.Cashier, Role.TenantAdmin, Role.Cashier], CreatedAt, Admin);

        (user.Email, user.NormalizedEmail, user.DisplayName, user.Status).ShouldBe(("Asha.Rao@Example.IN", "ASHA.RAO@EXAMPLE.IN", "Asha Rao", UserStatus.Active));
        user.Roles.Select(role => (role.Role, role.GrantedAtUtc, role.GrantedByUserId)).ShouldBe([(Role.TenantAdmin, CreatedAt, (UserId?)Admin), (Role.Cashier, CreatedAt, Admin)]);
        user.LastSignInAtUtc.ShouldBeNull();
        user.IsActiveTenantAdmin.ShouldBeTrue();
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserCreated("Asha.Rao@Example.IN", "Asha Rao", "TenantAdmin, Cashier"));
    }

    [Fact]
    public void BR601_the_address_compares_without_regard_to_case()
        => User.Normalize(" asha.rao@example.in ").ShouldBe(User.Normalize("ASHA.RAO@EXAMPLE.IN"));

    [Fact]
    public void A_user_without_a_role_or_with_one_the_enum_does_not_define_is_a_bug_in_the_caller()
    {
        Should.Throw<ArgumentException>(() => User.Create("a@b.in", "A", "hash", [], CreatedAt, Admin));
        Should.Throw<ArgumentException>(() => User.Create("a@b.in", "A", "hash", [(Role)42], CreatedAt, Admin));
        Should.Throw<ArgumentException>(() => User.Create(" ", "A", "hash", [Role.Viewer], CreatedAt, Admin));
        Should.Throw<ArgumentException>(() => User.Create("a@b.in", "A", " ", [Role.Viewer], CreatedAt, Admin));
        Should.Throw<ArgumentException>(() => User.Create(new string('a', User.EmailMaxLength + 1), "A", "hash", [Role.Viewer], CreatedAt, Admin));
    }

    [Fact]
    public void US01_the_address_and_the_name_change_together_and_only_a_change_is_an_event()
    {
        var user = Created();
        user.ClearDomainEvents();

        user.Update("asha.rao@example.in", "Asha Rao");
        user.DomainEvents.ShouldBeEmpty("nothing changed");

        user.Update(" asha@hospital.in ", "Dr Asha Rao");
        (user.Email, user.NormalizedEmail, user.DisplayName).ShouldBe(("asha@hospital.in", "ASHA@HOSPITAL.IN", "Dr Asha Rao"));
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserUpdated(user.Id, "asha@hospital.in", "Dr Asha Rao"));
    }

    [Fact]
    public void US01_replacing_the_roles_keeps_the_grant_of_a_kept_role_grants_the_new_ones_and_takes_the_rest_away()
    {
        var user = Created(Role.Cashier, Role.Viewer);
        user.ClearDomainEvents();

        user.ReplaceRoles([Role.Supervisor, Role.Cashier], Later, OtherAdmin);

        user.Roles.OrderBy(role => role.Role).Select(role => (role.Role, role.GrantedAtUtc, role.GrantedByUserId))
            .ShouldBe([(Role.Supervisor, Later, (UserId?)OtherAdmin), (Role.Cashier, CreatedAt, Admin)]);
        user.HasRole(Role.Viewer).ShouldBeFalse();
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserRolesReplaced(user.Id, "Supervisor, Cashier"));

        user.ClearDomainEvents();
        user.ReplaceRoles([Role.Cashier, Role.Supervisor, Role.Cashier], Later, OtherAdmin);
        user.DomainEvents.ShouldBeEmpty("the same roles in another order are no change");
        Should.Throw<ArgumentException>(() => user.ReplaceRoles([], Later, OtherAdmin));
        user.Roles.Count.ShouldBe(2);
    }

    [Fact]
    public void BR601_a_user_is_disabled_and_enabled_once_each_and_a_disabled_administrator_manages_nothing()
    {
        var user = Created(Role.TenantAdmin);
        user.ClearDomainEvents();

        user.Disable().IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Disabled);
        user.IsActiveTenantAdmin.ShouldBeFalse("a disabled administrator is no administrator");
        user.Disable().Error.Code.ShouldBe(IdentityErrors.UserAlreadyDisabledCode);

        user.Enable().IsSuccess.ShouldBeTrue();
        user.Enable().Error.Code.ShouldBe(IdentityErrors.UserAlreadyActiveCode);
        user.IsActiveTenantAdmin.ShouldBeTrue();
        user.DomainEvents.ShouldBe([new UserDisabled(user.Id, "asha.rao@example.in"), new UserEnabled(user.Id, "asha.rao@example.in")]);
    }

    [Fact]
    public void ADR0030_a_password_reset_replaces_the_hash_and_its_event_carries_neither_password_nor_hash()
    {
        var user = Created();
        user.ClearDomainEvents();

        user.ResetPassword("pbkdf2-sha256$600000$bmV3$bmV3");

        user.PasswordHash.ShouldBe("pbkdf2-sha256$600000$bmV3$bmV3");
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserPasswordReset(user.Id));
        Should.Throw<ArgumentException>(() => user.ResetPassword(" "));
    }

    [Fact]
    public void US02_a_sign_in_is_recorded_on_the_user_and_is_an_event_that_carries_nothing_secret()
    {
        var user = Created();
        user.ClearDomainEvents();

        user.RecordSignIn(Later);

        user.LastSignInAtUtc.ShouldBe(Later);
        user.DomainEvents.ShouldHaveSingleItem().ShouldBe(new UserSignedIn(user.Id, Later));
    }

    [Fact]
    public void BR601_recording_a_sign_in_of_a_disabled_user_is_a_bug_in_the_caller()
    {
        var user = Created();
        user.Disable();
        user.ClearDomainEvents();

        Should.Throw<InvalidOperationException>(() => user.RecordSignIn(Later)).Message.ShouldContain("BR-601");

        user.LastSignInAtUtc.ShouldBeNull();
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void The_roles_table_holds_the_five_roles_in_the_order_of_the_enum()
        => RoleDefinition.All.Select(definition => definition.Code).ShouldBe(Enum.GetValues<Role>());

    private static User Created(params Role[] roles)
        => User.Create("asha.rao@example.in", "Asha Rao", "pbkdf2-sha256$1$c2FsdA==$aGFzaA==", roles.Length > 0 ? roles : [Role.BillingClerk], CreatedAt, Admin);
}
