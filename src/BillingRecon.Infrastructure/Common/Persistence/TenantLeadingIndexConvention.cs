using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Refuses a model in which an index of a tenant-scoped table does not lead with <c>TenantId</c> (docs/database.md
/// section 1, ADR 0015). Every query of such a table is filtered by the tenant, so an index that does not start with it
/// serves no query well, and a unique index without it would be unique across tenants: one hospital's MRN would block
/// another's. The check runs whenever the model is built: at startup, in tests and in the EF Core tools.
/// </summary>
internal sealed class TenantLeadingIndexConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var problems = modelBuilder.Metadata.GetEntityTypes()
            .Where(entityType => typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            .SelectMany(entityType => entityType.GetDeclaredIndexes()
                .Where(index => index.Properties[0].Name != nameof(ITenantScoped.TenantId))
                .Select(index => $"{entityType.DisplayName()}: {index.GetDatabaseName() ?? "an index"} on ({string.Join(", ", index.Properties.Select(property => property.Name))})"))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Every index of a tenant-scoped table leads with TenantId (docs/database.md, section 1). These do not:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem)));
        }
    }
}
