using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// An administrator takes the second step of a user away, for a lost phone (ADR 0061): the secret and the recovery
/// codes are gone, every session of the user ends, the user enrols again at the next sign-in when their roles need
/// one, and decides nothing for 24 hours, because who reset it could then sign in as them (review finding R5). Audited
/// as <c>SecondStepReset</c> with the administrator as its user.
/// </summary>
public sealed class ResetUserSecondStepHandler(
    IClock clock,
    IUserRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ResetUserSecondStepCommand, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(ResetUserSecondStepCommand command, CancellationToken ct)
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

        user.ResetSecondStep(clock.UtcNow + User.ApprovalHold);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? user.ToDto() : committed.Error;
    }
}
