using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>Adds entities to the model of a <see cref="ModelProbeContext{TProbe}"/>.</summary>
public interface IModelProbe
{
    public void Configure(ModelBuilder modelBuilder);
}
