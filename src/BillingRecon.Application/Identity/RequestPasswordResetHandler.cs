using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A link for a forgotten password is asked for (ADR 0061, amended). **The answer is the same whatever happened:**
/// an unknown hospital or address, a disabled user, a user who was sent five links this hour and a deployment without a
/// mail server all answer as a link that was sent does, so the answer never tells which addresses exist. For a known,
/// active user the hash of a new link is stored, good for 30 minutes and replacing the one before, and the mail is
/// handed over to be sent after the request. The request has no token: like the sign-in it steps into the user it
/// found, to write that row and its audit record as that user (the second of three use cases that may, ADR 0031).
/// </summary>
public sealed class RequestPasswordResetHandler(
    IValidator<RequestPasswordResetCommand> validator,
    IClock clock,
    ILoginUserLookup lookup,
    ISignInScope signInScope,
    IUserRepository users,
    IPasswordResetTokens tokens,
    IPasswordResetMail mail,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestPasswordResetCommand, bool>
{
    /// <inheritdoc />
    public async Task<Result<bool>> HandleAsync(RequestPasswordResetCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (!mail.IsConfigured)
        {
            return true;
        }

        var code = command.Tenant.Trim().ToLowerInvariant();
        var tenant = await lookup.FindTenantAsync(code, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return true;
        }

        var candidate = await lookup.FindUserAsync(tenant.Id, User.Normalize(command.Email), ct);
        if (candidate is null || candidate.Status != UserStatus.Active)
        {
            return true;
        }

        signInScope.Enter(tenant.Id, candidate.Id, candidate.Roles);
        var user = await users.GetAsync(candidate.Id, ct)
            ?? throw new InvalidOperationException($"User {candidate.Id} was found to reset a password and then not in its own tenant.");

        var (token, hash) = tokens.New(user.Id);
        if (!user.RequestPasswordReset(hash, clock.UtcNow))
        {
            return true;
        }

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsSuccess)
        {
            // To the address the hospital keeps, not the one that was typed, which only found it.
            mail.Send(user.Email, user.DisplayName, code, token);
        }

        // A lost race with another change of the user sends nothing; the answer is the same, and the user asks again.
        return true;
    }
}
