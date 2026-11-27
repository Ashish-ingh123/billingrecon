using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Confirms an enrolment with the first code of the authenticator app (ADR 0061): the pending secret becomes the
/// user's second step, ten recovery codes are made and answered this once (only their hashes are kept), every session
/// the user had ends, and the answer is the sign-in of the new one, its second step given.
/// </summary>
public sealed class ConfirmSecondStepEnrolmentHandler(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserRepository users,
    ISecondStepSecrets secrets,
    ISecondStepPolicy policy,
    ITokenIssuer tokens,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<ConfirmSecondStepEnrolmentCommand, SecondStepEnrolledDto>
{
    /// <inheritdoc />
    public async Task<Result<SecondStepEnrolledDto>> HandleAsync(ConfirmSecondStepEnrolmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (tenantContext.TenantId is not { } tenantId || currentUser.UserId is not { } userId || await users.GetAsync(userId, ct) is not { } user)
        {
            return IdentityErrors.UserNotFound();
        }

        if (user.SecondStepEnrolled)
        {
            return IdentityErrors.SecondStepAlreadyEnrolled();
        }

        if (user.SecondStepPendingSecret is not { } pending)
        {
            return IdentityErrors.SecondStepEnrolmentNotBegun();
        }

        var secret = secrets.Unprotect(tenantId, userId, pending)
            ?? throw new InvalidOperationException($"The pending second step of user {userId} cannot be read with the key of this deployment.");
        var now = clock.UtcNow;
        if (!Totp.TryMatch(secret, (command.Code ?? string.Empty).Trim(), now, lastUsedStep: null, out var step))
        {
            return IdentityErrors.SecondStepCodeWrong();
        }

        var codes = secrets.NewRecoveryCodes();
        var confirmed = user.ConfirmSecondStep(step, [.. codes.Select(secrets.HashRecoveryCode)], now);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess
            ? new SecondStepEnrolledDto(codes, SignInTokens.For(tokens, policy, clock, tenantId, user, secondStepGiven: true))
            : committed.Error;
    }
}
