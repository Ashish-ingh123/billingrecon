using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The signed-in user sets a password of their own (ADR 0051): the current password is checked against the user's hash,
/// the new one is stored, no change is due any more, the change is audited, and a new token without the mark of a due
/// change is issued, so that the user goes on without signing in again.
/// </summary>
public sealed class ChangeOwnPasswordHandler(
    IValidator<ChangeOwnPasswordCommand> validator,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
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

        var roles = user.Roles.Select(role => role.Role).Order().ToArray();
        var token = tokens.Issue(tenantId, user.Id, roles, passwordChangeDue: false);
        var seconds = (int)Math.Round((token.ExpiresAtUtc - clock.UtcNow).TotalSeconds, MidpointRounding.AwayFromZero);
        return new SignedInDto(token.Value, SignInHandler.TokenType, token.ExpiresAtUtc, seconds, CredentialsChangeDue: false);
    }
}
