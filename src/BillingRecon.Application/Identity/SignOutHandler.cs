using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The signed-in user signs out (ADR 0057): the number of the user's sessions goes up, so every token issued to the user
/// so far, on every device, is refused from the next request on, and the sign-out is audited. The answer is true once that
/// is saved.
/// </summary>
public sealed class SignOutHandler(ICurrentUser currentUser, IUserRepository users, IUnitOfWork unitOfWork)
    : ICommandHandler<SignOutCommand, bool>
{
    /// <inheritdoc />
    public async Task<Result<bool>> HandleAsync(SignOutCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId || await users.GetAsync(userId, ct) is not { } user)
        {
            return IdentityErrors.UserNotFound();
        }

        user.EndSessions();
        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? true : committed.Error;
    }
}
