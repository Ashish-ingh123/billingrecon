using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A new password is set with the link of a mail (ADR 0061, amended). The link names its user; the hospital's code
/// came with it. It is good once, for 30 minutes, and only the last one sent: 422 <c>password_reset_link_invalid</c>
/// for everything else, the same answer for an unknown hospital, a link that is no link and a link that is spent.
/// Every session of the user ends. The answer is no token: the user signs in, second step included, as always.
/// </summary>
public sealed class ResetPasswordByLinkHandler(
    IValidator<ResetPasswordByLinkCommand> validator,
    IClock clock,
    ILoginUserLookup lookup,
    ISignInScope signInScope,
    IUserRepository users,
    IPasswordResetTokens tokens,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork) : ICommandHandler<ResetPasswordByLinkCommand, bool>
{
    /// <inheritdoc />
    public async Task<Result<bool>> HandleAsync(ResetPasswordByLinkCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var tenant = await lookup.FindTenantAsync(command.Tenant.Trim().ToLowerInvariant(), ct);
        if (tenant is null || tenant.Status != TenantStatus.Active || !tokens.TryRead(command.Token, out var userId, out var hash))
        {
            return IdentityErrors.PasswordResetLinkInvalid();
        }

        // The request acts as the user the link names, in the hospital the link came for: only so can it read the row
        // the link is checked against. Nothing is written unless the link is the one that row holds.
        signInScope.Enter(tenant.Id, userId, []);
        var user = await users.GetAsync(userId, ct);
        if (user is null)
        {
            return IdentityErrors.PasswordResetLinkInvalid();
        }

        var reset = user.ResetPasswordByLink(hash, hasher.Hash(command.NewPassword), clock.UtcNow);
        if (reset.IsFailure)
        {
            return reset.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? true : committed.Error;
    }
}
