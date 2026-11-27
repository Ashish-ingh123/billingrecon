using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The second step of a sign-in (ADR 0061): the user, known by the token the password gave, gives the code of their
/// authenticator app or a recovery code, and is answered a full token. A code of the app is good for its own
/// 30-second step and the two beside it, and once: the step it was accepted for is spent. A wrong code is counted and
/// saved, and the fifth in a row ends the sign-in.
/// </summary>
public sealed class VerifySecondStepHandler(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserRepository users,
    ISecondStepSecrets secrets,
    ISecondStepPolicy policy,
    ITokenIssuer tokens,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<VerifySecondStepCommand, SignedInDto>
{
    /// <inheritdoc />
    public async Task<Result<SignedInDto>> HandleAsync(VerifySecondStepCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (tenantContext.TenantId is not { } tenantId || currentUser.UserId is not { } userId || await users.GetAsync(userId, ct) is not { } user)
        {
            return IdentityErrors.UserNotFound();
        }

        if (user.SecondStepSecret is not { } stored)
        {
            return IdentityErrors.SecondStepNotEnrolled();
        }

        var code = (command.Code ?? string.Empty).Trim();
        var now = clock.UtcNow;
        var right = false;
        if (code.Length == Totp.Digits && code.All(char.IsAsciiDigit))
        {
            // A stored secret that does not open is a fault of the deployment (another key), never a wrong code.
            var secret = secrets.Unprotect(tenantId, userId, stored)
                ?? throw new InvalidOperationException($"The second step of user {userId} cannot be read with the key of this deployment.");
            if (Totp.TryMatch(secret, code, now, user.SecondStepLastStep, out var step))
            {
                user.PassSecondStep(step);
                right = true;
            }
        }
        else if (code.Length is >= 16 and <= 24)
        {
            right = user.UseRecoveryCode(secrets.HashRecoveryCode(code));
        }

        if (!right)
        {
            user.FailSecondStep();
        }

        // The failure is saved too: the count of wrong codes must outlive the request that gave one.
        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error;
        }

        return right ? SignInTokens.For(tokens, policy, clock, tenantId, user, secondStepGiven: true) : IdentityErrors.SecondStepCodeWrong();
    }
}
