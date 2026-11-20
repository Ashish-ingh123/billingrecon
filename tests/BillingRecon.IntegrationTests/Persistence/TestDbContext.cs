using System.Reflection;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The production context plus the probe entities (<see cref="Widget"/>, <see cref="LogLine"/>,
/// <see cref="Setting"/>). It configures only what an entity configuration would: lengths and precision. Everything
/// else must come from the conventions of <see cref="BillingReconDbContext"/>, which is what the tests check.
/// </summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext, ICurrentUser currentUser, IClock clock)
    : BillingReconDbContext(options, tenantContext, currentUser, clock)
{
    protected override IEnumerable<Assembly> TypedIdAssemblies => [.. base.TypedIdAssemblies, typeof(TestDbContext).Assembly];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.Property(entity => entity.Name).HasMaxLength(100);
            widget.Property(entity => entity.Kind).HasMaxLength(20);
            widget.Property(entity => entity.PreviousKind).HasMaxLength(20);
            widget.Property(entity => entity.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LogLine>(line => line.Property(entity => entity.Text).HasMaxLength(400));

        modelBuilder.Entity<Setting>(setting =>
        {
            setting.Property(entity => entity.Key).HasMaxLength(50).IsUnicode(false);
            setting.Property(entity => entity.Json).IsUnbounded();
        });

        // Numbered as the documents of the application will be: one number per tenant, and one sequence number per
        // tenant, document type and fiscal year (docs/database.md, sections 3 and 4.3).
        modelBuilder.Entity<NumberedDocument>(document =>
        {
            document.Property(entity => entity.DocumentType).HasMaxLength(20);
            document.Property(entity => entity.Number).HasMaxLength(30).IsUnicode(false);
            document.Property(entity => entity.Title).HasMaxLength(100);
            document.HasIndex(entity => new { entity.TenantId, entity.Number }).IsUnique();
            document.HasIndex(entity => new { entity.TenantId, entity.DocumentType, entity.FiscalYear, entity.SequenceNumber }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
