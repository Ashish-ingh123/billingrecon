using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Replaces the roles of a user (US-01). The tenant's last active administrator keeps the role: taking it away would
/// leave nobody to manage the users (422 <c>user_last_tenant_admin</c>, ADR 0030).
/// </summary>
public sealed class ReplaceUserRolesHandler(
    IValidator<ReplaceUserRolesCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IUserRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReplaceUserRolesCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(ReplaceUserRolesCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var user = await repository.GetAsync(command.Id, ct);
        if (user is null)
        {
            return IdentityErrors.UserNotFound();
        }

        if (RowVersionText.Of(user) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (user.IsActiveTenantAdmin && !command.Roles.Contains(Role.TenantAdmin) && await repository.CountOtherActiveTenantAdminsAsync(user.Id, ct) == 0)
        {
            return IdentityErrors.UserLastTenantAdmin();
        }

        user.ReplaceRoles(command.Roles, clock.UtcNow, currentUser.UserId);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? user.ToDto() : committed.Error;
    }
}
