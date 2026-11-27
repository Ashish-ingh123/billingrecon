using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>What the sign-in page may offer (ADR 0061, amended): whether a forgotten password can be reset by mail here.</summary>
public sealed class SignInOptionsHandler(IPasswordResetMail mail) : IQueryHandler<SignInOptionsQuery, SignInOptionsDto>
{
    /// <inheritdoc />
    public Task<Result<SignInOptionsDto>> HandleAsync(SignInOptionsQuery query, CancellationToken ct)
        => Task.FromResult<Result<SignInOptionsDto>>(new SignInOptionsDto(mail.IsConfigured));
}
