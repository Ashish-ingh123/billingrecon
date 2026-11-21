using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin changes the settings of the tenant (BR-607, US-03; ADR 0033): the approval threshold, the back-date
/// window and the default due days, at the version read (BR-804). The change is audited, and every use case reads the
/// settings when it runs, so it applies from the next request. A body that changes nothing changes nothing.
/// </summary>
public sealed class UpdateTenantSettingsHandler(
    IValidator<UpdateTenantSettingsCommand> validator,
    ITenantRepository tenants,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateTenantSettingsCommand, TenantSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> HandleAsync(UpdateTenantSettingsCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var tenant = await tenants.GetCurrentAsync(ct);
        if (RowVersionText.Of(tenant) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var threshold = command.PostingApprovalThreshold is { } amount ? Money.Of(amount) : (Money?)null;
        if (!tenant.ChangeSettings(threshold, command.MaxBackdateDays!.Value, command.DefaultDueDays!.Value))
        {
            return tenant.ToSettingsDto();
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? tenant.ToSettingsDto() : committed.Error;
    }
}
