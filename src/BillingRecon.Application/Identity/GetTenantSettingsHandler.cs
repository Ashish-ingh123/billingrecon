using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Reads the settings of the current tenant (BR-607), with its version for a change.</summary>
public sealed class GetTenantSettingsHandler(ITenantRepository tenants) : IQueryHandler<GetTenantSettingsQuery, TenantSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> HandleAsync(GetTenantSettingsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenant = await tenants.GetCurrentAsync(ct);

        return tenant.ToSettingsDto();
    }
}
