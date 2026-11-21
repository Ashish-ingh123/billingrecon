using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Disables a user (BR-601): no more sign-ins; the row and everything that names it stay. Refused for the current user
/// themselves, and for the tenant's last active administrator (ADR 0030).
/// </summary>
public sealed class DisableUserHandler(ICurrentUser currentUser, IUserRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<DisableUserCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(DisableUserCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await repository.GetAsync(command.Id, ct);
        if (user is null)
        {
            return IdentityErrors.UserNotFound();
        }

        if (RowVersionText.Of(user) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (currentUser.UserId == user.Id)
        {
            return IdentityErrors.UserCannotDisableSelf();
        }

        if (user.IsActiveTenantAdmin && await repository.CountOtherActiveTenantAdminsAsync(user.Id, ct) == 0)
        {
            return IdentityErrors.UserLastTenantAdmin();
        }

        var disabled = user.Disable();
        if (disabled.IsFailure)
        {
            return disabled.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? user.ToDto() : committed.Error;
    }
}
