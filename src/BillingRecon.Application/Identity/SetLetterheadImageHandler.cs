using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin sets or replaces the logo or the signature of the letterhead (ADR 0059). The bytes are stored as a new
/// row, unless the hospital stored the same bytes before, and the tenant names them by their SHA-256 under the version
/// of the settings; the tenant's event, and so the audit trail, carries the hash before and after and never the image.
/// The same image again changes nothing and audits nothing.
/// </summary>
public sealed class SetLetterheadImageHandler(
    IValidator<SetLetterheadImageCommand> validator,
    ITenantRepository tenants,
    ITenantImageRepository images,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<SetLetterheadImageCommand, TenantSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> HandleAsync(SetLetterheadImageCommand command, CancellationToken ct)
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

        var sha256 = TenantImage.HashOf(command.Content);
        if (!tenant.ChangeLetterheadImage(command.Kind, sha256))
        {
            return tenant.ToSettingsDto();
        }

        if (await images.FindAsync(command.Kind, sha256, ct) is null)
        {
            images.Add(TenantImage.Store(command.Kind, command.Content, clock.UtcNow));
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? tenant.ToSettingsDto() : committed.Error;
    }
}
