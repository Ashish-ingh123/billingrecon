using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Common;

/// <summary>
/// A decision that finds no pending approval request (BR-605, BR-804). The document it read was waiting, so another
/// decision was committed in between: the caller is told to read again, and nothing is written. This once threw, and
/// the client got 500.
/// </summary>
public sealed class PendingApprovalsTests
{
    private static readonly UserId Maker = new(Guid.NewGuid());
    private static readonly Guid Document = Guid.NewGuid();

    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();

    [Fact]
    public async Task The_pending_request_of_a_document_is_found()
    {
        var request = ApprovalRequest.Open(ApprovalDocumentType.Bill, Document, ApprovalAction.Post, Maker, makerComment: null, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        _approvals.GetPendingAsync(ApprovalDocumentType.Bill, Document, Arg.Any<CancellationToken>()).Returns(request);

        var found = await _approvals.FindPendingAsync(ApprovalDocumentType.Bill, Document, CancellationToken.None);

        found.Value.ShouldBeSameAs(request);
    }

    [Fact]
    public async Task BR804_a_request_that_is_gone_is_a_conflict_and_not_a_fault()
    {
        var found = await _approvals.FindPendingAsync(ApprovalDocumentType.Bill, Document, CancellationToken.None);

        found.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        found.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task BR804_a_rejection_of_a_bill_whose_request_another_checker_decided_meanwhile_is_a_conflict_and_commits_nothing()
    {
        // The bill is read as waiting; the request is no longer pending.
        var id = new BillId(Guid.NewGuid());
        var window = new BusinessDateWindow(TestDoubles.Today, 7);
        var bill = Bill.Create(
            new PatientId(Guid.NewGuid()),
            new PayerId(Guid.NewGuid()),
            new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today },
            [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
            window).Value.WithId(id).Saved(1, 2, 3);
        bill.SubmitForApproval(window).IsSuccess.ShouldBeTrue();
        var bills = Substitute.For<IBillRepository>();
        bills.GetAsync(id, Arg.Any<CancellationToken>()).Returns(bill);
        var checker = Substitute.For<ICurrentUser>();
        checker.UserId.Returns(new UserId(Guid.NewGuid()));
        var unitOfWork = TestDoubles.UnitOfWork();
        var handler = new RejectBillPostingHandler(new RejectBillPostingCommandValidator(), TestDoubles.ClockAt(TestDoubles.Today), checker, bills, _approvals, unitOfWork);

        var result = await handler.HandleAsync(new RejectBillPostingCommand(id, Convert.ToBase64String([1, 2, 3]), "No"), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }
}
