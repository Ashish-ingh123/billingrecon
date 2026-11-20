using BillingRecon.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// What maker-checker adds to the <c>Bills</c> table (docs/database.md, section 3.3; BR-207): the index of the bills
/// that wait for approval. A class of its own for the same reason as <see cref="BillPostingConfiguration"/>: it
/// belongs to migration <c>0010_Approvals</c>, which is scaffolded with this file back in the project and the
/// earlier ones without it (docs/runbook.md, section 8). It can join <see cref="BillConfiguration"/> once that
/// migration exists.
/// </summary>
public sealed class BillApprovalConfiguration : IEntityTypeConfiguration<Bill>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        // Few bills wait at any time, so the index holds only those.
        builder.HasIndex(bill => new { bill.TenantId, bill.Status })
            .HasFilter("[Status] = 'PendingApproval'")
            .HasDatabaseName("IX_Bills_TenantId_Status");
    }
}
