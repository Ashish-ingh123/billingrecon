using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin removes the logo or the signature from the letterhead (ADR 0059): the tenant stops naming it, under
/// the version of the settings, and the documents print without it. The stored bytes stay, so that the hash in the
/// audit trail still finds them. Removing an image the hospital does not have changes nothing and audits nothing.
/// </summary>
public sealed class RemoveLetterheadImageHandler(
    IValidator<RemoveLetterheadImageCommand> validator,
    ITenantRepository tenants,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveLetterheadImageCommand, TenantSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> HandleAsync(RemoveLetterheadImageCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var tenant = await tenants.GetCurrentAsync(ct);
        if (RowVersionText.Of(tenant) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (!tenant.ChangeLetterheadImage(command.Kind, null))
        {
            return tenant.ToSettingsDto();
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? tenant.ToSettingsDto() : committed.Error;
    }
}
