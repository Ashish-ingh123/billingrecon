using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin sets what the hospital prints at the head and foot of its documents (ADR 0054). It shares the version
/// of the settings, since both are the tenant's own row; a letterhead that changes nothing writes nothing and audits
/// nothing. The GSTIN is kept in capitals.
/// </summary>
public sealed class UpdateLetterheadHandler(
    IValidator<UpdateLetterheadCommand> validator,
    ITenantRepository tenants,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateLetterheadCommand, TenantSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> HandleAsync(UpdateLetterheadCommand command, CancellationToken ct)
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

        var changed = tenant.ChangeLetterhead(new TenantLetterhead
        {
            LegalName = command.LegalName,
            AddressLine1 = command.AddressLine1,
            AddressLine2 = command.AddressLine2,
            City = command.City,
            State = command.State,
            PinCode = command.PinCode,
            Phone = command.Phone,
            Email = command.Email,
            Gstin = SettingsRules.NormalizePrefix(command.Gstin),
            Signatory = command.Signatory,
        });
        if (!changed)
        {
            return tenant.ToSettingsDto();
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? tenant.ToSettingsDto() : committed.Error;
    }
}
