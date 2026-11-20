using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Common.Approvals;

/// <summary>
/// The <c>ApprovalRequests</c> table (docs/database.md, section 3.6; BR-603 to BR-605). Two rules of maker-checker
/// are constraints of the table as well as rules of the aggregate: the checker is never the maker, and a document
/// has at most one pending request. The document is named by kind and id, so there is no foreign key to it. The
/// tenant column, the enums as text with their CHECK constraints, the instants and the row version come from the
/// conventions of the context; the two user columns get their foreign keys when the users exist (ADR 0021).
/// </summary>
public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    private const int DocumentTypeMaxLength = 30;
    private const int ActionMaxLength = 30;
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests", table =>
        {
            table.HasCheckConstraint("CK_ApprovalRequests_CheckerIsNotMaker", "[CheckerUserId] IS NULL OR [CheckerUserId] <> [MakerUserId]");
            table.HasCheckConstraint(
                "CK_ApprovalRequests_Decided",
                "([Status] = 'Pending' AND [CheckerUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([Status] <> 'Pending' AND [CheckerUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_ApprovalRequests_RejectionHasComment", "[Status] <> 'Rejected' OR [CheckerComment] IS NOT NULL");
        });
        builder.HasKey(request => request.Id);

        builder.Property(request => request.DocumentType).HasMaxLength(DocumentTypeMaxLength);
        builder.Property(request => request.Action).HasMaxLength(ActionMaxLength);
        builder.Property(request => request.Status).HasMaxLength(StatusMaxLength);
        builder.Property(request => request.MakerComment).HasMaxLength(ApprovalRequest.CommentMaxLength);
        builder.Property(request => request.CheckerComment).HasMaxLength(ApprovalRequest.CommentMaxLength);

        // At most one pending request per document (BR-605); decided requests stay as its history.
        builder.HasIndex(request => new { request.TenantId, request.DocumentType, request.DocumentId })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("UX_ApprovalRequests_TenantId_DocumentType_DocumentId");

        // The checker's inbox: the pending requests of the tenant, oldest first.
        builder.HasIndex(request => new { request.TenantId, request.Status, request.RequestedAtUtc })
            .HasDatabaseName("IX_ApprovalRequests_TenantId_Status_RequestedAtUtc");
    }
}
