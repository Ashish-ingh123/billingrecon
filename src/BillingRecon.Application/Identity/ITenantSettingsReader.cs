namespace BillingRecon.Application.Identity;

/// <summary>
/// Reads the settings of the current tenant for the use cases of other modules. Identity is a cross-cutting module, so
/// any module may ask here (ADR 0003); none reads the tenant row itself.
/// </summary>
public interface ITenantSettingsReader
{
    /// <summary>The date settings of the current tenant.</summary>
    /// <exception cref="InvalidOperationException">The current scope has no tenant, or its row does not exist: a fault, not a business failure.</exception>
    public Task<TenantDocumentSettings> GetDocumentSettingsAsync(CancellationToken ct);
}
