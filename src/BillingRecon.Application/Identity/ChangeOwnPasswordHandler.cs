using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The signed-in user sets a password of their own (ADR 0051): the current password is checked against the user's hash,
/// the new one is stored, no change is due any more, every session the user had ends (ADR 0057), the change is audited,
/// and a new token of the new session, without the mark of a due change, is issued, so that the user goes on without
/// signing in again.
/// </summary>
public sealed class ChangeOwnPasswordHandler(
    IValidator<ChangeOwnPasswordCommand> validator,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    ISecondStepPolicy secondStep,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<ChangeOwnPasswordCommand, SignedInDto>
{
    /// <inheritdoc />
    public async Task<Result<SignedInDto>> HandleAsync(ChangeOwnPasswordCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (tenantContext.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return IdentityErrors.UserNotFound();
        }

        var user = await users.GetAsync(userId, ct);
        if (user is null)
        {
            return IdentityErrors.UserNotFound();
        }

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            return IdentityErrors.PasswordCurrentWrong();
        }

        user.ChangePassword(hasher.Hash(command.NewPassword));
        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error;
        }

        // The new token is of the new session; a user with a second step gives its code again (ADR 0061).
        return SignInTokens.For(tokens, secondStep, clock, tenantId, user, secondStepGiven: false);
    }
}
