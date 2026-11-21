using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>
/// The approval request (BR-603 to BR-605; docs/domain-model.md, sections 4.9 and 5.4): opened pending by a maker,
/// decided once by another user who holds Supervisor or TenantAdmin, approved or rejected with a comment.
/// </summary>
public sealed class ApprovalRequestTests
{
    private static readonly Role[] AsSupervisor = [Role.Supervisor];
    private static readonly Guid Bill = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly UserId Maker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3303"));
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");
    private static readonly DateTimeOffset RequestedAt = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BR604_a_request_is_opened_pending_with_its_maker_its_document_and_its_action()
    {
        var request = ApprovalRequest.Open(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, Maker, " Large package ", RequestedAt, Correlation);

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.DocumentType.ShouldBe(ApprovalDocumentType.Bill);
        request.DocumentId.ShouldBe(Bill);
        request.Action.ShouldBe(ApprovalAction.Post);
        request.MakerUserId.ShouldBe(Maker);
        request.MakerComment.ShouldBe("Large package");
        request.RequestedAtUtc.ShouldBe(RequestedAt);
        request.CorrelationId.ShouldBe(Correlation);
        request.CheckerUserId.ShouldBeNull();
        request.CheckerComment.ShouldBeNull();
        request.DecidedAtUtc.ShouldBeNull();
        request.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ApprovalRequestOpened(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, Maker));
    }

    [Fact]
    public void BR606_another_user_approves_and_the_decision_is_recorded()
    {
        var request = Pending();

        var result = request.Approve(Checker, AsSupervisor, comment: null, DecidedAt);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.CheckerUserId.ShouldBe(Checker);
        request.CheckerComment.ShouldBeNull("a comment is optional for an approval");
        request.DecidedAtUtc.ShouldBe(DecidedAt);
        request.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ApprovalRequestApproved(request.Id, ApprovalDocumentType.Bill, Bill, Checker));
    }

    [Fact]
    public void BR605_another_user_rejects_with_a_comment()
    {
        var request = Pending();

        var result = request.Reject(Checker, AsSupervisor, " Wrong patient ", DecidedAt);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.CheckerUserId.ShouldBe(Checker);
        request.CheckerComment.ShouldBe("Wrong patient");
        request.DecidedAtUtc.ShouldBe(DecidedAt);
        var raised = request.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ApprovalRequestRejected>();
        raised.ShouldBe(new ApprovalRequestRejected(request.Id, ApprovalDocumentType.Bill, Bill, Checker, "Wrong patient"));
        ((IHasReason)raised).Reason.ShouldBe("Wrong patient");
    }

    [Fact]
    public void BR603_the_maker_can_neither_approve_nor_reject_their_own_request()
    {
        var request = Pending();

        var approved = request.Approve(Maker, AsSupervisor, null, DecidedAt);
        var rejected = request.Reject(Maker, AsSupervisor, "Changed my mind", DecidedAt);

        foreach (var refused in new[] { approved, rejected })
        {
            refused.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
            refused.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        }

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.CheckerUserId.ShouldBeNull();
        request.DecidedAtUtc.ShouldBeNull();
        request.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Role.BillingClerk)]
    [InlineData(Role.Cashier)]
    [InlineData(Role.Viewer)]
    public void BR603_a_checker_who_is_neither_a_supervisor_nor_a_tenant_admin_can_neither_approve_nor_reject(Role role)
    {
        var request = Pending();

        var approved = request.Approve(Checker, [role], null, DecidedAt);
        var rejected = request.Reject(Checker, [role, Role.Viewer], "Wrong patient", DecidedAt);
        var withoutRoles = request.Approve(Checker, [], null, DecidedAt);

        foreach (var refused in new[] { approved, rejected, withoutRoles })
        {
            refused.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerRoleCode);
            refused.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        }

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.CheckerUserId.ShouldBeNull();
        request.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Role.Supervisor)]
    [InlineData(Role.TenantAdmin)]
    public void BR603_a_supervisor_or_a_tenant_admin_decides_also_when_holding_other_roles(Role role)
    {
        var approving = Pending();
        var rejecting = Pending();

        approving.Approve(Checker, [Role.Cashier, role], null, DecidedAt).IsSuccess.ShouldBeTrue();
        rejecting.Reject(Checker, [role], "Wrong patient", DecidedAt).IsSuccess.ShouldBeTrue();

        (approving.Status, rejecting.Status).ShouldBe((ApprovalStatus.Approved, ApprovalStatus.Rejected));
    }

    [Fact]
    public void BR603_the_maker_is_refused_as_the_maker_whatever_the_roles()
    {
        // Both rules fail for a clerk who decides their own request; the answer names the one about the maker.
        Pending().Approve(Maker, [Role.BillingClerk], null, DecidedAt).Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        Should.Throw<ArgumentNullException>(() => Pending().Approve(Checker, null!, null, DecidedAt));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BR605_a_decision_is_final(bool approvedFirst)
    {
        var request = Pending();
        if (approvedFirst)
        {
            request.Approve(Checker, AsSupervisor, null, DecidedAt);
        }
        else
        {
            request.Reject(Checker, AsSupervisor, "Wrong patient", DecidedAt);
        }

        request.ClearDomainEvents();
        var status = request.Status;
        var another = new UserId(Guid.NewGuid());

        var approvedAgain = request.Approve(another, AsSupervisor, null, DecidedAt.AddHours(1));
        var rejectedAgain = request.Reject(another, AsSupervisor, "Too late", DecidedAt.AddHours(1));

        foreach (var refused in new[] { approvedAgain, rejectedAgain })
        {
            refused.Error.Code.ShouldBe(CommonErrors.ApprovalAlreadyDecidedCode);
            refused.Error.Kind.ShouldBe(ErrorKind.Conflict);
        }

        request.Status.ShouldBe(status);
        request.CheckerUserId.ShouldBe(Checker);
        request.DecidedAtUtc.ShouldBe(DecidedAt);
        request.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_rejection_without_a_comment_is_a_bug_and_throws_and_the_request_stays_pending()
    {
        var request = Pending();

        Should.Throw<ArgumentException>(() => request.Reject(Checker, AsSupervisor, "  ", DecidedAt));
        Should.Throw<ArgumentException>(() => request.Reject(Checker, AsSupervisor, new string('c', ApprovalRequest.CommentMaxLength + 1), DecidedAt));
        Should.Throw<ArgumentException>(() => request.Approve(Checker, AsSupervisor, new string('c', ApprovalRequest.CommentMaxLength + 1), DecidedAt));

        request.Status.ShouldBe(ApprovalStatus.Pending);
    }

    [Fact]
    public void A_request_without_a_document_a_maker_or_a_correlation_id_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => ApprovalRequest.Open(ApprovalDocumentType.Bill, Guid.Empty, ApprovalAction.Post, Maker, null, RequestedAt, Correlation));
        Should.Throw<ArgumentException>(() => ApprovalRequest.Open(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, default, null, RequestedAt, Correlation));
        Should.Throw<ArgumentException>(() => ApprovalRequest.Open(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, Maker, null, RequestedAt, Guid.Empty));
        Should.Throw<ArgumentException>(() => ApprovalRequest.Open((ApprovalDocumentType)99, Bill, ApprovalAction.Post, Maker, null, RequestedAt, Correlation));
        Should.Throw<ArgumentException>(() => Pending().Approve(default, AsSupervisor, null, DecidedAt));
    }

    [Fact]
    public void A_new_request_has_no_id_no_tenant_and_no_row_version_until_it_is_saved()
    {
        var request = ApprovalRequest.Open(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, Maker, null, RequestedAt, Correlation);

        request.Id.ShouldBe(default);
        request.TenantId.ShouldBe(default);
        request.RowVersion.ShouldBeEmpty();
    }

    private static ApprovalRequest Pending()
    {
        var request = ApprovalRequest.Open(ApprovalDocumentType.Bill, Bill, ApprovalAction.Post, Maker, null, RequestedAt, Correlation);
        request.ClearDomainEvents();

        return request;
    }
}
