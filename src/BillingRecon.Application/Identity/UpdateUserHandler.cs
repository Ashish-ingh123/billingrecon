using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>Changes the address and the display name of a user; a new address must be free in the tenant (BR-601).</summary>
public sealed class UpdateUserHandler(
    IValidator<UpdateUserCommand> validator,
    IUserRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateUserCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(UpdateUserCommand command, CancellationToken ct)
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

        if (await repository.EmailTakenAsync(User.Normalize(command.Email), except: user.Id, ct))
        {
            return IdentityErrors.UserEmailTaken();
        }

        user.Update(command.Email, command.DisplayName);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? IdentityErrors.UserEmailTaken() : committed.Error;
        }

        return user.ToDto();
    }
}
