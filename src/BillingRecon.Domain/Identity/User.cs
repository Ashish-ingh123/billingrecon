using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// A person who signs in to one tenant (BR-601; docs/domain-model.md, section 4.12): an e-mail address, unique in the
/// tenant without regard to case, a display name, a password hash, a status, and one or more roles.
/// <para>
/// The aggregate holds the hash as an opaque text; hashing and checking a password are the Infrastructure's
/// (<c>IPasswordHasher</c>). Whether an address is free, and whether the user is the tenant's last active
/// administrator, are questions about other users that the use case asks (ADR 0030).
/// </para>
/// </summary>
public sealed class User : AggregateRoot<UserId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Email"/>.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>Longest allowed <see cref="DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 200;

    /// <summary>Longest allowed <see cref="PasswordHash"/>.</summary>
    public const int PasswordHashMaxLength = 500;

    private readonly List<UserRole> _roles = [];

    // Entity Framework creates a user with this constructor and then sets the properties.
    private User()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The e-mail address as it was entered, trimmed.</summary>
    public string Email { get; private set; }

    /// <summary>The address upper-cased, which the unique index of the tenant compares (BR-601).</summary>
    public string NormalizedEmail { get; private set; }

    /// <summary>The name shown for the user.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The hash of the password, never the password. Kept out of every audit image (ADR 0030).</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Whether the user may sign in.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>When the user last signed in; null until the first sign-in (Prompt 5.2).</summary>
    public DateTimeOffset? LastSignInAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>The roles of the user, one row each, in no promised order.</summary>
    public IReadOnlyList<UserRole> Roles => _roles;

    /// <summary>Whether the user may manage the tenant now: active and a <see cref="Role.TenantAdmin"/>.</summary>
    public bool IsActiveTenantAdmin => Status == UserStatus.Active && HasRole(Role.TenantAdmin);

    /// <summary>The upper-cased form of <paramref name="email"/>, trimmed, as the unique index compares it.</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Creates an active user with <paramref name="roles"/>, each granted at <paramref name="createdAtUtc"/> by
    /// <paramref name="grantedBy"/> (null when no user acts, as for the first user of a tenant).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A text is blank or too long, there is no role, or a role is not one the enum defines. The validator of the use
    /// case refuses all of these first.
    /// </exception>
    public static User Create(string email, string displayName, string passwordHash, IReadOnlyCollection<Role> roles, DateTimeOffset createdAtUtc, UserId? grantedBy)
    {
        var address = Field.Required(email, EmailMaxLength);
        var name = Field.Required(displayName, DisplayNameMaxLength);
        var hash = Field.Required(passwordHash, PasswordHashMaxLength);
        var granted = Distinct(roles);

        var user = new User
        {
            Email = address,
            NormalizedEmail = Normalize(address),
            DisplayName = name,
            PasswordHash = hash,
            Status = UserStatus.Active,
        };

        foreach (var role in granted)
        {
            user._roles.Add(UserRole.Grant(role, createdAtUtc, grantedBy));
        }

        // The id is still empty here: it is assigned when the user is added to the unit of work.
        user.AddDomainEvent(new UserCreated(address, name, Names(granted)));

        return user;
    }

    /// <summary>Whether the user holds <paramref name="role"/>.</summary>
    public bool HasRole(Role role) => _roles.Any(candidate => candidate.Role == role);

    /// <summary>
    /// Changes the e-mail address and the display name. Raises <see cref="UserUpdated"/> when a value changed; that the
    /// new address is free in the tenant is the use case's question.
    /// </summary>
    /// <exception cref="ArgumentException">A text is blank or too long.</exception>
    public void Update(string email, string displayName)
    {
        var address = Field.Required(email, EmailMaxLength);
        var name = Field.Required(displayName, DisplayNameMaxLength);

        if ((address, name) == (Email, DisplayName))
        {
            return;
        }

        Email = address;
        NormalizedEmail = Normalize(address);
        DisplayName = name;
        AddDomainEvent(new UserUpdated(Id, Email, DisplayName));
    }

    /// <summary>
    /// Replaces the roles of the user with <paramref name="roles"/>: a role kept keeps when and by whom it was granted, a
    /// new one is granted now by <paramref name="grantedBy"/>, one left out is taken away. Raises
    /// <see cref="UserRolesReplaced"/> when the set changed. Whether this leaves the tenant without an administrator is
    /// the use case's question.
    /// </summary>
    /// <exception cref="ArgumentException">There is no role, or a role is not one the enum defines.</exception>
    public void ReplaceRoles(IReadOnlyCollection<Role> roles, DateTimeOffset grantedAtUtc, UserId? grantedBy)
    {
        var wanted = Distinct(roles);
        if (wanted.SequenceEqual(Distinct([.. _roles.Select(candidate => candidate.Role)])))
        {
            return;
        }

        _roles.RemoveAll(candidate => !wanted.Contains(candidate.Role));
        foreach (var role in wanted.Where(role => !HasRole(role)))
        {
            _roles.Add(UserRole.Grant(role, grantedAtUtc, grantedBy));
        }

        AddDomainEvent(new UserRolesReplaced(Id, Names(wanted)));
    }

    /// <summary>Disables the user: no more sign-ins (BR-601). A disabled user is refused.</summary>
    public Result Disable()
    {
        if (Status == UserStatus.Disabled)
        {
            return IdentityErrors.UserAlreadyDisabled();
        }

        Status = UserStatus.Disabled;
        AddDomainEvent(new UserDisabled(Id, Email));

        return Result.Success();
    }

    /// <summary>Lets a disabled user sign in again. An active user is refused.</summary>
    public Result Enable()
    {
        if (Status == UserStatus.Active)
        {
            return IdentityErrors.UserAlreadyActive();
        }

        Status = UserStatus.Active;
        AddDomainEvent(new UserEnabled(Id, Email));

        return Result.Success();
    }

    /// <summary>An administrator sets a new password: the hash is replaced. The event says so and carries neither.</summary>
    /// <exception cref="ArgumentException">The hash is blank or too long.</exception>
    public void ResetPassword(string passwordHash)
    {
        PasswordHash = Field.Required(passwordHash, PasswordHashMaxLength);
        AddDomainEvent(new UserPasswordReset(Id));
    }

    // The roles once each, in the order of the enum; at least one, each defined.
    private static Role[] Distinct(IReadOnlyCollection<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (roles.Count == 0)
        {
            throw new ArgumentException("A user holds at least one role (ADR 0030).", nameof(roles));
        }

        if (roles.Any(role => !Enum.IsDefined(role)))
        {
            throw new ArgumentException("A role is one the enum defines.", nameof(roles));
        }

        return [.. roles.Distinct().Order()];
    }

    private static string Names(IEnumerable<Role> roles) => string.Join(", ", roles);
}
