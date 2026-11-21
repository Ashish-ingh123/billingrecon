using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// An administrator sets a new password for a user (ADR 0030): the given password is hashed and replaces the old hash.
/// No change is forced at the next sign-in. The audit trail records that it happened, and neither the password nor the
/// hash.
/// </summary>
public sealed class ResetUserPasswordHandler(
    IValidator<ResetUserPasswordCommand> validator,
    IPasswordHasher hasher,
    IUserRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ResetUserPasswordCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(ResetUserPasswordCommand command, CancellationToken ct)
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

        user.ResetPassword(hasher.Hash(command.Password));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? user.ToDto() : committed.Error;
    }
}
