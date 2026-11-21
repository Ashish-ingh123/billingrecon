using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Lets a disabled user sign in again.</summary>
public sealed class EnableUserHandler(IUserRepository repository, IUnitOfWork unitOfWork) : ICommandHandler<EnableUserCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(EnableUserCommand command, CancellationToken ct)
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

        var enabled = user.Enable();
        if (enabled.IsFailure)
        {
            return enabled.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? user.ToDto() : committed.Error;
    }
}
