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

    /// <summary>
    /// Whether the user must set a password of their own before doing anything else (ADR 0051, amending ADR 0030): true for a
    /// user an administrator created or whose password an administrator reset, false once the user has changed it.
    /// </summary>
    public bool PasswordChangeDue { get; private set; }

    /// <summary>
    /// The secret of the user's second step, protected (encrypted) as the port that stores it made it; null while the
    /// user has none (ADR 0061). Never audited and never answered.
    /// </summary>
    public string? SecondStepSecret { get; private set; }

    /// <summary>The protected secret of an enrolment that was begun and not confirmed yet; null otherwise.</summary>
    public string? SecondStepPendingSecret { get; private set; }

    /// <summary>The last time step a code was accepted for: that step and every earlier one are spent (a code is good once).</summary>
    public long? SecondStepLastStep { get; private set; }

    /// <summary>Wrong codes in a row since the last right one.</summary>
    public int SecondStepFailures { get; private set; }

    /// <summary>When the second step was enrolled; null while the user has none.</summary>
    public DateTimeOffset? SecondStepEnrolledAtUtc { get; private set; }

    /// <summary>The hashes of the recovery codes not used yet, separated by a space; null while the user has no second step.</summary>
    public string? RecoveryCodeHashes { get; private set; }

    /// <summary>
    /// Until when the user approves and rejects nothing: 24 hours after an administrator reset their password or their
    /// second step (ADR 0061; review finding R5). Null when no reset was made.
    /// </summary>
    public DateTimeOffset? ApprovalHoldUntilUtc { get; private set; }

    /// <summary>True when the user has a second step.</summary>
    public bool SecondStepEnrolled => SecondStepSecret is not null;

    /// <summary>
    /// The number of the user's sessions (ADR 0057): a token carries the number it was issued with and is refused once the
    /// user has another. It goes up at a sign-out, a change or a reset of the password, a disabling and a change of the roles,
    /// so each of them ends every session the user had.
    /// </summary>
    public int SessionVersion { get; private set; }

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
    /// <paramref name="grantedBy"/> (null when no user acts, as for the first user of a tenant). A user an administrator
    /// creates is given <paramref name="passwordChangeDue"/>: the password the administrator chose is changed at the first
    /// sign-in (ADR 0051).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A text is blank or too long, there is no role, or a role is not one the enum defines. The validator of the use
    /// case refuses all of these first.
    /// </exception>
    public static User Create(string email, string displayName, string passwordHash, IReadOnlyCollection<Role> roles, DateTimeOffset createdAtUtc, UserId? grantedBy, bool passwordChangeDue = false)
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
            PasswordChangeDue = passwordChangeDue,
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

        SessionVersion++;
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
        SessionVersion++;
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

    /// <summary>
    /// An administrator sets a new password: the hash is replaced, and the user must change it at the next sign-in
    /// (ADR 0051). The event says so and carries neither.
    /// </summary>
    /// <exception cref="ArgumentException">The hash is blank or too long.</exception>
    public void ResetPassword(string passwordHash, DateTimeOffset? approvalHoldUntilUtc = null)
    {
        ApprovalHoldUntilUtc = approvalHoldUntilUtc ?? ApprovalHoldUntilUtc;
        PasswordHash = Field.Required(passwordHash, PasswordHashMaxLength);
        PasswordChangeDue = true;
        SessionVersion++;
        AddDomainEvent(new UserPasswordReset(Id));
    }

    /// <summary>
    /// The user sets a password of their own (ADR 0051): the hash is replaced and no change is due any more. That the
    /// current password was given is the use case's question. The event says so and carries neither password.
    /// </summary>
    /// <exception cref="ArgumentException">The hash is blank or too long.</exception>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = Field.Required(passwordHash, PasswordHashMaxLength);
        PasswordChangeDue = false;
        SessionVersion++;
        AddDomainEvent(new UserPasswordChanged(Id));
    }

    /// <summary>Longest protected secret of a second step, as it is stored.</summary>
    public const int ProtectedSecretMaxLength = 200;

    /// <summary>Longest text of the hashes of the recovery codes: ten of 64 characters and their spaces.</summary>
    public const int RecoveryCodeHashesMaxLength = 700;

    /// <summary>How many wrong codes in a row end the sign-in that gave them: its token is refused from then on.</summary>
    public const int SecondStepFailuresAllowed = 5;

    /// <summary>How long a user decides nothing after an administrator reset their password or their second step.</summary>
    public static readonly TimeSpan ApprovalHold = TimeSpan.FromHours(24);

    /// <summary>The user began to enrol a second step: the protected secret waits for its first code.</summary>
    public Result BeginSecondStepEnrolment(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);

        if (SecondStepEnrolled)
        {
            return IdentityErrors.SecondStepAlreadyEnrolled();
        }

        SecondStepPendingSecret = protectedSecret;
        AddDomainEvent(new UserSecondStepEnrolmentBegun(Id));

        return Result.Success();
    }

    /// <summary>
    /// The first code of the pending secret was right, for <paramref name="step"/>: the user has a second step from now
    /// on, with the recovery codes whose hashes are given. Every session the user had ends (ADR 0057).
    /// </summary>
    public Result ConfirmSecondStep(long step, IReadOnlyCollection<string> recoveryCodeHashes, DateTimeOffset enrolledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodeHashes);

        if (SecondStepEnrolled)
        {
            return IdentityErrors.SecondStepAlreadyEnrolled();
        }

        if (SecondStepPendingSecret is null)
        {
            return IdentityErrors.SecondStepEnrolmentNotBegun();
        }

        SecondStepSecret = SecondStepPendingSecret;
        SecondStepPendingSecret = null;
        SecondStepLastStep = step;
        SecondStepFailures = 0;
        SecondStepEnrolledAtUtc = enrolledAtUtc;
        RecoveryCodeHashes = string.Join(' ', recoveryCodeHashes);
        SessionVersion++;
        AddDomainEvent(new UserSecondStepEnrolled(Id));

        return Result.Success();
    }

    /// <summary>A right code was given for <paramref name="step"/>, which is spent with every step before it.</summary>
    public void PassSecondStep(long step)
    {
        SecondStepLastStep = step;
        SecondStepFailures = 0;
        AddDomainEvent(new UserSecondStepPassed(Id));
    }

    /// <summary>
    /// Spends the recovery code whose hash is <paramref name="hash"/>: true when the user had it. A recovery code is
    /// good once.
    /// </summary>
    public bool UseRecoveryCode(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var left = (RecoveryCodeHashes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!SecondStepEnrolled || !left.Remove(hash))
        {
            return false;
        }

        RecoveryCodeHashes = string.Join(' ', left);
        SecondStepFailures = 0;
        AddDomainEvent(new UserRecoveryCodeUsed(Id, left.Count));

        return true;
    }

    /// <summary>
    /// A wrong code was given. After <see cref="SecondStepFailuresAllowed"/> in a row the sign-in that gave them is
    /// over: every session of the user ends, the one between its two steps with them. True when that happened.
    /// </summary>
    public bool FailSecondStep()
    {
        SecondStepFailures++;
        var locked = SecondStepFailures >= SecondStepFailuresAllowed;
        if (locked)
        {
            SecondStepFailures = 0;
            SessionVersion++;
        }

        AddDomainEvent(new UserSecondStepFailed(Id, locked));

        return locked;
    }

    /// <summary>
    /// An administrator took the user's second step away (a lost phone): the secret and the recovery codes are gone,
    /// every session ends, and the user decides nothing until <paramref name="approvalHoldUntilUtc"/>.
    /// </summary>
    public void ResetSecondStep(DateTimeOffset approvalHoldUntilUtc)
    {
        SecondStepSecret = null;
        SecondStepPendingSecret = null;
        SecondStepLastStep = null;
        SecondStepFailures = 0;
        SecondStepEnrolledAtUtc = null;
        RecoveryCodeHashes = null;
        ApprovalHoldUntilUtc = approvalHoldUntilUtc;
        SessionVersion++;
        AddDomainEvent(new UserSecondStepReset(Id));
    }

    /// <summary>
    /// The user signs out (ADR 0057): every token issued to the user so far is refused from now on, on every device. Raises
    /// <see cref="UserSignedOut"/>.
    /// </summary>
    public void EndSessions()
    {
        SessionVersion++;
        AddDomainEvent(new UserSignedOut(Id));
    }

    /// <summary>
    /// The user signed in at <paramref name="signedInAtUtc"/> (US-02; ADR 0031): the time is kept, and the audit trail
    /// says so. Only an active user signs in; the use case refuses a disabled one before it comes here.
    /// </summary>
    /// <exception cref="InvalidOperationException">The user is disabled: a bug in the caller.</exception>
    public void RecordSignIn(DateTimeOffset signedInAtUtc)
    {
        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException("A disabled user does not sign in (BR-601).");
        }

        LastSignInAtUtc = signedInAtUtc;
        AddDomainEvent(new UserSignedIn(Id, signedInAtUtc));
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
