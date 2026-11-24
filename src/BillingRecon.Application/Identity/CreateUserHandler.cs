using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Creates a user of the current tenant (US-01; BR-601): the address must be free in the tenant, the password is
/// hashed at once, and the roles are granted by the current user. Audited (BR-701), without the hash.
/// </summary>
public sealed class CreateUserHandler(
    IValidator<CreateUserCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    IUserRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateUserCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(CreateUserCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (await repository.EmailTakenAsync(User.Normalize(command.Email), except: null, ct))
        {
            return IdentityErrors.UserEmailTaken();
        }

        var user = User.Create(command.Email, command.DisplayName, hasher.Hash(command.Password), command.Roles, clock.UtcNow, currentUser.UserId, passwordChangeDue: true);
        repository.Add(user);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The only unique index a new user can hit is the address's: another request took it in between.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? IdentityErrors.UserEmailTaken() : committed.Error;
        }

        return user.ToDto();
    }
}
