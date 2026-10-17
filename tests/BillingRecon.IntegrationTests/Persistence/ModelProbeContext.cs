using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The production context plus whatever <typeparamref name="TProbe"/> adds to the model. Entity Framework builds
/// one model per context type, so every probe gets a model of its own; that is how the tests build models that the
/// conventions must reject.
/// </summary>
public sealed class ModelProbeContext<TProbe>(
    DbContextOptions<ModelProbeContext<TProbe>> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IClock clock)
    : BillingReconDbContext(options, tenantContext, currentUser, clock)
    where TProbe : IModelProbe, new()
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new TProbe().Configure(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}
