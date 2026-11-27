using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Begins the enrolment of a second step (ADR 0061): a new secret is made, stored protected as the pending one, and
/// answered once, as text and as the address of a QR code. Beginning again replaces a pending secret that was never
/// confirmed. A user who has a second step cannot begin another: an administrator takes the old one away first.
/// </summary>
public sealed class BeginSecondStepEnrolmentHandler(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserRepository users,
    ISecondStepSecrets secrets,
    ISecondStepPolicy policy,
    IUnitOfWork unitOfWork) : ICommandHandler<BeginSecondStepEnrolmentCommand, SecondStepEnrolmentDto>
{
    /// <inheritdoc />
    public async Task<Result<SecondStepEnrolmentDto>> HandleAsync(BeginSecondStepEnrolmentCommand command, CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId || currentUser.UserId is not { } userId || await users.GetAsync(userId, ct) is not { } user)
        {
            return IdentityErrors.UserNotFound();
        }

        var secret = secrets.NewSecret();
        var begun = user.BeginSecondStepEnrolment(secrets.Protect(tenantId, userId, secret));
        if (begun.IsFailure)
        {
            return begun.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess
            ? new SecondStepEnrolmentDto(Totp.ToBase32(secret), Totp.ProvisioningUri(policy.Issuer, user.Email, secret).AbsoluteUri)
            : committed.Error;
    }
}
