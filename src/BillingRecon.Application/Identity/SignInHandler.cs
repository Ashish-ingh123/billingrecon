using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A user signs in (US-02; BR-601, BR-602; ADR 0031): the tenant by its code, the user by its address in that tenant,
/// the password against the user's hash. Every refusal is the same to the caller and costs one password verification,
/// whether or not the address exists; the outcome says why, for the log. On success the request acts as the user
/// (<see cref="ISignInScope"/>), the sign-in is recorded and audited, and a token is issued.
/// </summary>
public sealed class SignInHandler(
    IValidator<SignInCommand> validator,
    IClock clock,
    ILoginUserLookup lookup,
    IPasswordHasher hasher,
    ISignInScope signInScope,
    IUserRepository users,
    ITokenIssuer tokens,
    IUnitOfWork unitOfWork) : ICommandHandler<SignInCommand, SignInOutcome>
{
    /// <summary>The type of the token, as the client sends it back in the <c>Authorization</c> header.</summary>
    public const string TokenType = "Bearer";

    /// <inheritdoc />
    public async Task<Result<SignInOutcome>> HandleAsync(SignInCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var tenant = await lookup.FindTenantAsync(command.Tenant.Trim().ToLowerInvariant(), ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            hasher.VerifyWithoutHash(command.Password);

            return SignInOutcome.Refused(tenant is null ? SignInRefusal.UnknownTenant : SignInRefusal.TenantSuspended);
        }

        var candidate = await lookup.FindUserAsync(tenant.Id, User.Normalize(command.Email), ct);
        if (candidate is null)
        {
            hasher.VerifyWithoutHash(command.Password);

            return SignInOutcome.Refused(SignInRefusal.UnknownUser);
        }

        if (!hasher.Verify(command.Password, candidate.PasswordHash))
        {
            return SignInOutcome.Refused(SignInRefusal.WrongPassword, candidate.Id);
        }

        if (candidate.Status != UserStatus.Active)
        {
            return SignInOutcome.Refused(SignInRefusal.UserDisabled, candidate.Id);
        }

        // From here on the request acts as the user it has verified, and only now can it read and write its row.
        signInScope.Enter(tenant.Id, candidate.Id, candidate.Roles);
        var user = await users.GetAsync(candidate.Id, ct)
            ?? throw new InvalidOperationException($"User {candidate.Id} was found to sign in and then not in its own tenant.");

        var now = clock.UtcNow;
        user.RecordSignIn(now);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error;
        }

        var token = tokens.Issue(tenant.Id, candidate.Id, candidate.Roles);
        var seconds = (int)Math.Round((token.ExpiresAtUtc - now).TotalSeconds, MidpointRounding.AwayFromZero);

        return SignInOutcome.Success(new SignedInDto(token.Value, TokenType, token.ExpiresAtUtc, seconds), candidate.Id);
    }
}
